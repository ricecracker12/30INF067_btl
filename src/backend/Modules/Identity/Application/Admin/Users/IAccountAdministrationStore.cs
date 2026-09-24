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
}
