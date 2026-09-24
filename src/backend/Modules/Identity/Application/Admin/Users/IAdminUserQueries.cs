namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Đường ĐỌC của màn quản trị tài khoản (GĐ6 D2). Hiện thực EF ở <c>Infrastructure/Persistence</c> — Application không chạm EF
/// (<c>PersistenceBoundaryTests</c>). Trả hàng thô của <c>users ⋈ roles</c>; tên hiển thị (Profile) do
/// <see cref="AdminUserReadService"/> hydrate một lô, không join chéo schema (Đ-2.2).
/// </summary>
public interface IAdminUserQueries
{
    /// <summary>
    /// Tối đa <paramref name="take"/> tài khoản sau <paramref name="after"/>, sắp <c>created_at DESC, user_id DESC</c>. Người gọi
    /// xin <c>limit + 1</c> để biết còn trang sau. MỘT câu SQL.
    /// </summary>
    Task<IReadOnlyList<AdminUserRow>> ListAsync(
        AdminUserFilter filter, AdminUserCursor? after, int take, CancellationToken ct);

    /// <summary>Một tài khoản; <c>null</c> nếu không tồn tại.</summary>
    Task<AdminUserRow?> FindAsync(Guid userId, CancellationToken ct);
}

/// <summary>Bộ lọc đã qua validator. <c>null</c> = không lọc theo trường đó.</summary>
public sealed record AdminUserFilter(string? EmailPrefix, string? Status, string? RoleCode);

/// <summary>Một dòng <c>users ⋈ roles</c> — chưa có tên hiển thị, <see cref="LockedUntil"/> còn nguyên giá trị trong DB.</summary>
public sealed record AdminUserRow(
    Guid UserId,
    string Email,
    string RoleCode,
    string RoleDisplayName,
    string Status,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt);
