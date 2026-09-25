namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Một tài khoản trên màn quản trị — schema <c>AdminUser</c> của <c>admin-v1.yaml</c> (GĐ6 Mục 8.2). DTO riêng, không trả entity
/// <c>User</c> (Swagger sẽ sinh schema có <c>passwordHash</c>).
///
/// <list type="bullet">
/// <item><paramref name="Email"/> là PII (Mục 8.2): chỉ trả ở nhóm <c>admin-v1</c>, chỉ cho người có quyền quản trị, không bao giờ
/// vào log.</item>
/// <item><paramref name="DisplayName"/> hydrate từ Profile qua <c>IUserDirectory</c>, một lô cho cả trang. Chưa onboarding thì
/// <c>null</c>.</item>
/// <item><paramref name="LockedUntil"/> là khóa tạm của FR-003 (đăng nhập sai), KHÁC <c>status = disabled</c> của Admin
/// (Đ-6.5). Chỉ có giá trị khi mốc còn ở tương lai — mốc đã qua là tài khoản đã tự mở, trả ra chỉ làm Admin tưởng còn khóa.</item>
/// </list>
/// </summary>
public sealed record AdminUser(
    Guid UserId,
    string Email,
    string? DisplayName,
    string RoleCode,
    string RoleDisplayName,
    string Status,
    bool EmailVerified,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt);

/// <summary>
/// Body 200 của <c>GET /admin/users</c>. Sắp <c>created_at DESC, user_id DESC</c>. <paramref name="NextCursor"/> <c>null</c> khi
/// hết, không phải <c>""</c>.
/// </summary>
public sealed record AdminUserPage(IReadOnlyList<AdminUser> Items, string? NextCursor);
