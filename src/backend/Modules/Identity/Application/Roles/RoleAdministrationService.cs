using SocialApp.Modules.Identity.Application.Admin;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Roles;

/// <summary>
/// CRUD vai trò + ma trận quyền (GĐ6 D5, Đ-6.9, Đ-6.10) — "nâng cấp là thay dữ liệu, không thay code" (PTTK 6.7.2). Tầng 2
/// (<c>role.manage</c>) và fail-closed đã xong ở controller.
///
/// Thứ tự <b>DB → invalidate</b>: store trả về thì transaction đã <c>COMMIT</c>, rồi mới <see cref="IPermissionChangeNotifier.NotifyAsync"/>
/// — phát trước <c>COMMIT</c> thì instance khác xóa cache, nạp lại từ DB CHƯA commit và giữ quyền cũ 60 giây (cạm bẫy 2). MỘT lời gọi
/// cho cả xóa tại chỗ lẫn phát cho instance khác (L-C4). Không cần thu hồi token: token mang MÃ vai trò, không mang quyền.
/// </summary>
public sealed class RoleAdministrationService(IRoleStore store, IPermissionChangeNotifier notifier)
{
    public Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken ct) => store.ListAsync(ct);

    public Task<IReadOnlyList<PermissionInfo>> ListPermissionsAsync(CancellationToken ct) => store.ListPermissionsAsync(ct);

    /// <summary>
    /// Tạo vai trò. Phát invalidate cả ở đây (ngoài hướng dẫn D5): mã của một vai trò ĐÃ XÓA có thể còn trong cache dưới dạng tập
    /// rỗng (token cũ mang mã đó vẫn sống tới 15 phút) — tạo lại cùng mã thì 60 giây đầu người được gán không có quyền nào.
    /// </summary>
    public async Task<Result<RoleSummary>> CreateAsync(Guid actorId, CreateRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.CreateAsync(
            request.Code!, request.DisplayName!.Trim(), ToSet(request.Permissions!), actorId, ct);
        if (result.Status == RoleWriteStatus.CodeTaken)
            return AdminErrors.RoleCodeTaken;

        await notifier.NotifyAsync(result.Code!, CancellationToken.None);
        return await SummaryAsync(result.RoleId!.Value, ct);
    }

    /// <summary>Đổi tên hiển thị. Không phát invalidate: cache giữ QUYỀN, không giữ tên; <c>/me</c> đọc tên từ DB.</summary>
    public async Task<Result<RoleSummary>> RenameAsync(
        short roleId, Guid actorId, RenameRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.RenameAsync(roleId, request.DisplayName!.Trim(), actorId, ct);
        if (result.Status == RoleWriteStatus.NotFound)
            return AdminErrors.RoleNotFound;

        return await SummaryAsync(roleId, ct);
    }

    public async Task<Result<RoleSummary>> SetPermissionsAsync(
        short roleId, Guid actorId, SetRolePermissionsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.SetPermissionsAsync(
            roleId, ToSet(request.Permissions!), request.Confirm == true, actorId, ct);
        switch (result.Status)
        {
            case RoleWriteStatus.NotFound:
                return AdminErrors.RoleNotFound;
            case RoleWriteStatus.SystemRole:
                return AdminErrors.SystemRole;
            case RoleWriteStatus.NeedsPermission:
                return AdminErrors.RoleNeedsPermission;
            case RoleWriteStatus.ConfirmationRequired:
                return AdminErrors.ConfirmationRequired(result.Diff!.Added, result.Diff.Removed, result.AffectedUsers);
            case RoleWriteStatus.Changed:
                await notifier.NotifyAsync(result.Code!, CancellationToken.None);   // SAU COMMIT
                break;
        }

        return await SummaryAsync(roleId, ct);
    }

    public async Task<Result> DeleteAsync(short roleId, Guid actorId, CancellationToken ct)
    {
        var result = await store.DeleteAsync(roleId, actorId, ct);
        switch (result.Status)
        {
            case RoleWriteStatus.NotFound:
                return AdminErrors.RoleNotFound;
            case RoleWriteStatus.SystemRole:
                return AdminErrors.SystemRole;
            case RoleWriteStatus.InUse:
                return AdminErrors.RoleInUse;
        }

        await notifier.NotifyAsync(result.Code!, CancellationToken.None);   // SAU COMMIT
        return Result.Success();
    }

    private async Task<Result<RoleSummary>> SummaryAsync(short roleId, CancellationToken ct) =>
        await store.FindAsync(roleId, ct) is { } summary ? summary : AdminErrors.RoleNotFound;

    /// <summary>Mã trùng trong request gộp lại — PUT thay CẢ tập, tập thì không trùng.</summary>
    private static IReadOnlySet<string> ToSet(IEnumerable<string> codes) => new HashSet<string>(codes, StringComparer.Ordinal);
}
