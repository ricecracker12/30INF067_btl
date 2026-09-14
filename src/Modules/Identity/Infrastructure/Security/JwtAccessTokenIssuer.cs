using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Phát access token đúng cách Program.cs validate: HS256, khóa là byte UTF-8 của Jwt:SigningKey, claim tên ngắn.
/// <c>iat</c> do handler ghi từ <see cref="SecurityTokenDescriptor.IssuedAt"/> dạng giây Unix — OnTokenValidated
/// của D8 đọc đúng dạng đó. Giờ lấy từ <see cref="TimeProvider"/> (Đ-D10).
/// </summary>
internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenIssuer
{
    private readonly JwtOptions _jwt = options.Value;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)), SecurityAlgorithms.HmacSha256);

    public AccessToken Issue(Guid userId, string roleCode)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(_jwt.AccessTokenSeconds),   // CÙNG hằng số với TTL revoked:user (D8)
            Claims = new Dictionary<string, object>
            {
                [JwtClaims.Sub] = userId.ToString(),
                [JwtClaims.Role] = roleCode,
                [JwtClaims.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = _credentials,
        });
        return new AccessToken(token, _jwt.AccessTokenSeconds);
    }
}
