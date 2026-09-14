namespace SocialApp.Modules.Identity.Application.Me;

/// <summary>
/// Body 200 của <c>GET /me</c> — khớp <c>MeResponse</c> của hợp đồng. Là DTO riêng, KHÔNG trả entity <c>User</c>: Swagger
/// sẽ sinh schema có <c>passwordHash</c>. <paramref name="Role"/> (roles.code, cho máy so sánh) và
/// <paramref name="RoleDisplayName"/> (roles.display_name, cho người đọc) đều đọc từ DB, không từ claim trong token.
/// </summary>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    string Role,
    string RoleDisplayName,
    DateTimeOffset? EmailVerifiedAt,
    string Status,
    DateTimeOffset CreatedAt);
