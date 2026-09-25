namespace SocialApp.Modules.Identity.Application.Roles;

/// <summary>
/// Vai trò và ma trận quyền (GĐ6 D5, Đ-6.9). Hiện thực EF ở <c>Infrastructure/Persistence</c>. Mỗi thao tác ghi là MỘT transaction
/// Identity có audit trong đó (Đ-6.3), đã <c>COMMIT</c> khi trả về — người gọi phát invalidate cache quyền SAU đó (Đ-6.10, cạm bẫy 2).
/// </summary>
public interface IRoleStore
{
    /// <summary>Mọi vai trò theo <c>role_id</c>. Số câu SQL không đổi theo số vai trò.</summary>
    Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken ct);

    Task<RoleSummary?> FindAsync(short roleId, CancellationToken ct);

    /// <summary>18 mã theo <c>permission_id</c>.</summary>
    Task<IReadOnlyList<PermissionInfo>> ListPermissionsAsync(CancellationToken ct);

    /// <summary>
    /// Vai trò mới, <c>role_id</c> từ sequence của A3 (≥ 100). <c>code</c> trùng → <see cref="RoleWriteStatus.CodeTaken"/> (bắt
    /// unique violation của <c>IX_roles_code</c>, không SELECT trước — đọc-rồi-ghi thì hai request cùng qua). Audit
    /// <c>role.create { code, added }</c>.
    /// </summary>
    Task<RoleWriteResult> CreateAsync(
        string code, string displayName, IReadOnlySet<string> permissions, Guid actorId, CancellationToken ct);

    /// <summary>Đổi <c>display_name</c> — kể cả vai trò hệ thống (trigger A3 chỉ chặn <c>code</c>). Cùng tên → NoChange, không audit.</summary>
    Task<RoleWriteResult> RenameAsync(short roleId, string displayName, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Thay cả tập quyền. Thứ tự kiểm (hết trước mọi ghi): không tồn tại → ADMIN (<see cref="RoleWriteStatus.SystemRole"/>) → không đổi
    /// gì → vai trò hệ thống về 0 quyền (<see cref="RoleWriteStatus.NeedsPermission"/>) → vai trò hệ thống mà chưa
    /// <paramref name="confirmed"/> (<see cref="RoleWriteStatus.ConfirmationRequired"/> kèm diff + số người mang). Rồi DELETE/INSERT
    /// <c>role_permissions</c> + audit <c>role.permissions { code, added, removed, confirmed }</c>.
    /// </summary>
    Task<RoleWriteResult> SetPermissionsAsync(
        short roleId, IReadOnlySet<string> permissions, bool confirmed, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Xóa vai trò tự tạo không còn ai mang. Vai trò hệ thống → SystemRole; còn người → InUse. Lưới dưới DB: FK RESTRICT của
    /// <c>users.role_id</c> (<c>23503</c>) → InUse, trigger A3 (<c>P0001</c>) → SystemRole — không bao giờ 500. Audit <c>role.delete</c>.
    /// </summary>
    Task<RoleWriteResult> DeleteAsync(short roleId, Guid actorId, CancellationToken ct);
}

public enum RoleWriteStatus
{
    Changed,
    NoChange,
    NotFound,
    SystemRole,
    CodeTaken,
    InUse,
    NeedsPermission,
    ConfirmationRequired,
}

/// <summary>
/// Kết cục của một thao tác ghi vai trò. <see cref="Code"/> có khi đã biết vai trò (để phát invalidate theo mã);
/// <see cref="Diff"/>, <see cref="AffectedUsers"/> chỉ có ở <see cref="RoleWriteStatus.ConfirmationRequired"/>.
/// </summary>
public sealed record RoleWriteResult(
    RoleWriteStatus Status, short? RoleId = null, string? Code = null, RolePermissionDiff? Diff = null, int AffectedUsers = 0);
