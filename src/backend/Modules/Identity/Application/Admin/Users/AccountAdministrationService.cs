using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Khóa / mở khóa tài khoản (GĐ6 D3, UC-20). Đây là chỗ thứ tự <b>DB → Redis</b> (Đ-6.6, B.10 #1) hiện ra trên màn hình: store trả
/// về thì transaction đã <c>COMMIT</c>, và <see cref="UserRevoker.RevokeAsync"/> chạy SAU đó. Không nhánh nào <c>return</c> giữa
/// hai bước mà bỏ quên thu hồi — nhánh <c>Changed</c> luôn đi qua cả hai.
///
/// Tầng 2 (<c>user.lock</c> / <c>user.unlock</c>) và fail-closed đã xong ở controller. <c>actorId</c> từ token, <c>targetId</c> từ
/// đường (Mục 1.3 luật 2).
/// </summary>
public sealed class AccountAdministrationService(
    IAccountAdministrationStore store, UserRevoker revoker, AdminUserReadService users, TimeProvider time)
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
