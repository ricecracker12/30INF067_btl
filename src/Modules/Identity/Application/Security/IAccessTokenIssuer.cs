namespace SocialApp.Modules.Identity.Application.Security;

/// <summary>Access token vừa phát + số giây còn sống — đúng hai trường của <c>TokenResponse</c> trong hợp đồng.</summary>
public sealed record AccessToken(string Token, int ExpiresIn);

/// <summary>Phát access token JWT (login D3, refresh D5). Hiện thực ở Infrastructure/Security.</summary>
public interface IAccessTokenIssuer
{
    /// <summary><paramref name="roleCode"/> là roles.code CHUỖI đọc từ DB — không bao giờ role_id (Mục 3.1).</summary>
    AccessToken Issue(Guid userId, string roleCode);
}
