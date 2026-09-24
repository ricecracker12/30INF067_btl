using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Khóa / mở khóa tài khoản (GĐ6 D3), đổi vai trò (D4) — UC-20. Đây là chỗ thứ tự <b>DB → Redis</b> (Đ-6.6, B.10 #1) hiện ra trên màn hình: store trả
/// về thì transaction đã <c>COMMIT</c>, và <see cref="UserRevoker.RevokeAsync"/> chạy SAU đó. Không nhánh nào <c>return</c> giữa
/// hai bước mà bỏ quên thu hồi — nhánh <c>Changed</c> luôn đi qua cả hai.
///
/// Tầng 2 (<c>user.lock</c> / <c>user.unlock</c>) và fail-closed đã xong ở controller. <c>actorId</c> từ token, <c>targetId</c> từ
/// đường (Mục 1.3 luật 2).
/// </summary>
public sealed class AccountAdministrationService(
    IAccountAdministrationStore store,
    UserRevoker revoker,
    AdminUserReadService users,
    IPermissionCache permissions,
    IAuditTrail audit,
    TimeProvider time)
{
    public async Task<Result<AdminUserChange>> LockAsync(
        Guid targetId, Guid actorId, LockRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tự khóa → 400, trước mọi I/O: khóa xong thì không còn phiên để mở lại (Đ-6.7).
        if (targetId == actorId)
            return AdminErrors.SelfLock;

        // 2. Một transaction: khóa tư vấn → khóa dòng → disabled + thu hồi mọi family → đếm Admin → audit → COMMIT.
        var outcome = await store.LockAsync(targetId, actorId, request.Reason!.Trim(), time.GetUtcNow(), ct);
        switch (outcome)
        {
            case AdminOutcome.NotFound:
                return IdentityErrors.UserNotFound;
            case AdminOutcome.LastAdmin:
                return AdminErrors.LastAdmin;   // đã ROLLBACK — không ghi Redis
        }

        // 3. SAU COMMIT (Đ-6.6). NoChange: đã bị khóa từ trước, phiên của người đó đã bị thu hồi lúc ấy (L-D10).
        var revocation = outcome == AdminOutcome.Changed
            ? await revoker.RevokeAsync(targetId)
            : RevocationStates.NotNeeded;

        return await ChangeAsync(targetId, revocation, ct);
    }

    /// <summary>
    /// Mở khóa KHÔNG thu hồi gì — tài khoản bị khóa không có token sống để thu hồi (bảng Đ-6.6). <c>revocation</c> luôn
    /// <see cref="RevocationStates.NotNeeded"/> (L-D10). Tự mở khóa chính mình không chặn: người bị khóa không gọi được API.
    /// </summary>
    public async Task<Result<AdminUserChange>> UnlockAsync(Guid targetId, Guid actorId, CancellationToken ct)
    {
        var outcome = await store.UnlockAsync(targetId, actorId, time.GetUtcNow(), ct);
        if (outcome == AdminOutcome.NotFound)
            return IdentityErrors.UserNotFound;

        return await ChangeAsync(targetId, RevocationStates.NotNeeded, ct);
    }

    /// <summary>
    /// Đổi vai trò (D4). Tự hạ vai trò của mình ĐƯỢC — bất biến lo phần còn lại (Đ-6.7). Không thu hồi refresh family: token cũ chết
    /// ở mốc <c>revoked:user</c>, refresh cùng family cấp token mang vai trò mới đọc từ DB, người đó không bị đăng xuất (Mục 7.3).
    ///
    /// Tầng 2 kép (L-D18, chốt 2026-09-24): <c>role.assign</c> đã qua ở controller; thao tác CHẠM ADMIN — vai trò đích là ADMIN, hoặc
    /// người bị đổi đang là ADMIN — cần thêm <c>role.manage</c>. Không có thì người mang vai trò "Nhân sự" (chỉ <c>role.assign</c>, Đ-6.9)
    /// tự nâng mình lên toàn quyền. Tra quyền TRƯỚC <c>BEGIN</c> (khuôn L-D12): không giữ khóa dòng trong lúc tra cache; vế "người bị đổi
    /// đang là ADMIN" chỉ biết sau khi khóa dòng nên store kiểm nó với cờ tra sẵn.
    /// </summary>
    public async Task<Result<AdminUserChange>> AssignRoleAsync(
        Guid targetId, Guid actorId, string? actorRole, AssignRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var roleCode = request.RoleCode!;

        var canManageRoles = await permissions.IsAllowedAsync(actorRole, PermissionCodes.RoleManage, ct);
        if (roleCode == RoleCodes.Admin && !canManageRoles)
            return await DeniedAsync(actorId, targetId, ct);

        var outcome = await store.AssignRoleAsync(targetId, actorId, roleCode, canManageRoles, time.GetUtcNow(), ct);
        switch (outcome)
        {
            case AdminOutcome.NotFound:
                return IdentityErrors.UserNotFound;
            case AdminOutcome.UnknownRole:
                return AdminErrors.UnknownRole;
            case AdminOutcome.LastAdmin:
                return AdminErrors.LastAdmin;   // đã ROLLBACK — không ghi Redis
            case AdminOutcome.Forbidden:
                return await DeniedAsync(actorId, targetId, ct);
        }

        // SAU COMMIT (Đ-6.6). NoChange: đã mang đúng vai trò đó — token đang sống không sai gì (L-D10).
        var revocation = outcome == AdminOutcome.Changed
            ? await revoker.RevokeAsync(targetId)
            : RevocationStates.NotNeeded;

        return await ChangeAsync(targetId, revocation, ct);
    }

    /// <summary>
    /// 403 của tầng 2 kép + một dòng <c>access.denied</c> (Đ-6.15 "mọi lần bị từ chối", L-D12): handler C4 chỉ thấy từ chối ở
    /// middleware, không thấy lần từ chối trong service. <c>tx: null</c> — lúc bị từ chối không có thao tác nào để chung số phận
    /// (store đã rollback hoặc chưa mở transaction). Đối tượng là tài khoản đích, <c>metadata.permission</c> là quyền còn thiếu.
    /// </summary>
    private async Task<Result<AdminUserChange>> DeniedAsync(Guid actorId, Guid targetId, CancellationToken ct)
    {
        await audit.AppendAsync(null, new AuditEntry(
            actorId, AuditActions.AccessDenied, "user", targetId,
            new Dictionary<string, object?> { ["permission"] = PermissionCodes.RoleManage }), ct);
        return Result<AdminUserChange>.Forbidden();
    }

    /// <summary>
    /// Đọc lại tài khoản SAU thay đổi, để FE vẽ theo response (Đ-6.21, không optimistic). Tài khoản vừa ghi không thể biến mất giữa
    /// hai bước (xóa tài khoản là GĐ8); nếu có thì 404 thay vì 500.
    /// </summary>
    private async Task<Result<AdminUserChange>> ChangeAsync(Guid targetId, string revocation, CancellationToken ct)
    {
        var user = await users.GetAsync(targetId, ct);
        return user.IsSuccess
            ? new AdminUserChange(user.Value!, revocation)
            : user.Error!.Value;
    }
}
