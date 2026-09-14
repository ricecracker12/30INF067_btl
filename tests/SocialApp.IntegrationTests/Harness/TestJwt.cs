using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SocialApp.IntegrationTests.AuthZ;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Ký JWT cho test — đúng cách app validate ở C4: HS256, khóa là byte UTF-8 của chuỗi Jwt:SigningKey, claim
/// tên ngắn. Khóa sinh NGẪU NHIÊN mỗi lần chạy: repo không giữ khóa nào, kể cả khóa test (AGENTS.md Mục 14.3).
/// Vai trò và tên claim ghi bằng chuỗi hợp đồng, cố ý không đọc RoleCodes/JwtClaims.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "https://test.socialapp.local";
    public const string Audience = "socialapp-api-test";
    public static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:AccessTokenSeconds", "900");
    }

    public static string? ForCaller(Caller caller) => caller switch
    {
        Caller.Anonymous => null,
        Caller.User => Create("USER"),
        Caller.Moderator => Create("MODERATOR"),
        Caller.Admin => Create("ADMIN"),
        // Hết hạn từ 45 phút trước — vượt xa mọi ClockSkew, không phụ thuộc cấu hình lệch giờ.
        Caller.ExpiredToken => Create("USER", issuedAt: DateTimeOffset.UtcNow.AddHours(-1)),
        Caller.WrongSignature => Create("USER", signingKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
        _ => throw new ArgumentOutOfRangeException(nameof(caller)),
    };

    public static string Create(string role, Guid? userId = null, DateTimeOffset? issuedAt = null, string? signingKey = null)
    {
        var iat = issuedAt ?? DateTimeOffset.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = iat.UtcDateTime,
            NotBefore = iat.UtcDateTime,
            Expires = iat.AddMinutes(15).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (userId ?? Guid.NewGuid()).ToString(),
                ["role"] = role,
                ["jti"] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
