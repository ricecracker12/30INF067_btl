namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Đường GHI của màn quản trị tài khoản (GĐ6 D3, D4). Mỗi phương thức là MỘT transaction Identity, đã <c>COMMIT</c> (hoặc
/// rollback) khi trả về — người gọi ghi Redis SAU đó (Đ-6.6). Hiện thực ở Infrastructure vì đó là chỗ có transaction để truyền
/// cho <c>IAuditTrail</c> (Đ-6.3).
///
/// Khuôn của mọi phương thức (Đ-6.7, L-D8): khóa tư vấn <c>AdminInvariant</c> LUÔN lấy, TRƯỚC mọi khóa dòng → khóa dòng đích →
/// ghi → đếm Admin SAU khi ghi → audit trong transaction → <c>COMMIT</c>.
/// </summary>
public interface IAccountAdministrationStore
{
    /// <summary>
    /// <c>status = disabled</c> + thu hồi MỌI refresh family của <paramref name="targetId"/> + audit <c>user.lock</c> có
    /// <c>metadata.reason</c>. Đã <c>disabled</c> (hay không <c>active</c>) → <see cref="AdminOutcome.NoChange"/>, không audit (L-D10).
    /// </summary>
    Task<AdminOutcome> LockAsync(Guid targetId, Guid actorId, string reason, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// <c>status = active</c>, xóa <c>locked_until</c>, bộ đếm sai về 0 (Đ-6.5) + audit <c>user.unlock</c>. Đang <c>active</c> và
    /// không bị khóa tạm FR-003 → <see cref="AdminOutcome.NoChange"/>, không audit.
    /// </summary>
    Task<AdminOutcome> UnlockAsync(Guid targetId, Guid actorId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Đổi vai trò (D4) + audit <c>role.assign { fromRole, toRole }</c>. KHÔNG thu hồi refresh family: người được đổi không bị đăng
    /// xuất (Mục 7.3). <paramref name="roleCode"/> so chính xác; không tồn tại → <see cref="AdminOutcome.UnknownRole"/>. Đã mang vai
    /// trò đó → <see cref="AdminOutcome.NoChange"/>. Người bị đổi đang là ADMIN mà <paramref name="actorCanManageRoles"/> sai →
    /// <see cref="AdminOutcome.Forbidden"/> (L-D18).
    /// </summary>
    Task<AdminOutcome> AssignRoleAsync(
        Guid targetId, Guid actorId, string roleCode, bool actorCanManageRoles, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Kết cục của một thao tác ghi. <see cref="Changed"/> là trường hợp DUY NHẤT đã <c>COMMIT</c> một thay đổi.</summary>
public enum AdminOutcome
{
    Changed,

    /// <summary>Không có gì để đổi — không ghi DB, không audit, không đụng Redis (L-D10).</summary>
    NoChange,

    NotFound,

    /// <summary>Sau khi ghi thì còn 0 Admin hoạt động → đã ROLLBACK (Đ-6.7).</summary>
    LastAdmin,

    /// <summary>D4: vai trò đích không tồn tại.</summary>
    UnknownRole,

    /// <summary>D4 (L-D18): thao tác chạm vai trò ADMIN mà người gọi không có <c>role.manage</c> — tầng 2 kép, trả 403.</summary>
    Forbidden,
}
