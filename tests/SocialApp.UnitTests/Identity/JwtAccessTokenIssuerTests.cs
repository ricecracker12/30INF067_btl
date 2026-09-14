using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.SharedKernel.Authentication;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Token vừa phát giải mã lại bằng <see cref="JsonWebTokenHandler"/>. Tên claim và thuật toán viết tay, cố ý không đọc
/// JwtClaims. Lệch thật giữa phía phát và JwtBearer của Program.cs do <c>AuthHarnessTests</c> bắt (token từ DI của app
/// đi qua tầng 1 thật); test validate ở đây chép lại tham số của Program.cs để đỏ sớm, không cần dựng app.
/// </summary>
public sealed class JwtAccessTokenIssuerTests
{
    // 600 chứ không 900: khác mặc định, để issuer ghi cứng 900 thì test đỏ.
    private static readonly JwtOptions Jwt = new()
    {
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        Issuer = "https://unit.socialapp.local",
        Audience = "socialapp-api-unit",
        AccessTokenSeconds = 600,
    };

    private static IAccessTokenIssuer Issuer(TimeProvider? time = null) =>
        IdentityServices.Build(Jwt, time).GetRequiredService<IAccessTokenIssuer>();

    [Fact]
    public void Token_co_du_claim_HS256_va_song_dung_AccessTokenSeconds()
    {
        var userId = Guid.NewGuid();

        var issued = Issuer().Issue(userId, "MODERATOR");
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(issued.Token);

        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(userId.ToString(), jwt.GetPayloadValue<string>("sub"));
        Assert.Equal("MODERATOR", jwt.GetPayloadValue<string>("role"));
        Assert.True(Guid.TryParse(jwt.GetPayloadValue<string>("jti"), out _));
        Assert.Equal(600, jwt.GetPayloadValue<long>("exp") - jwt.GetPayloadValue<long>("iat"));
        Assert.Equal(600, issued.ExpiresIn);
        Assert.Equal("https://unit.socialapp.local", jwt.Issuer);
        Assert.Equal(["socialapp-api-unit"], jwt.Audiences);
    }

    /// <summary>Đ-D10: giờ phát token lấy từ TimeProvider, không từ DateTime.UtcNow.</summary>
    [Fact]
    public void Iat_lay_tu_TimeProvider_dang_giay_Unix()
    {
        var at = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(Issuer(new FixedTime(at)).Issue(Guid.NewGuid(), "USER").Token);

        Assert.Equal(at.ToUnixTimeSeconds(), jwt.GetPayloadValue<long>("iat"));
    }

    [Fact]
    public void Moi_token_mot_jti_rieng()
    {
        var issuer = Issuer();
        var userId = Guid.NewGuid();

        var first = new JsonWebTokenHandler().ReadJsonWebToken(issuer.Issue(userId, "USER").Token);
        var second = new JsonWebTokenHandler().ReadJsonWebToken(issuer.Issue(userId, "USER").Token);

        Assert.NotEqual(first.GetPayloadValue<string>("jti"), second.GetPayloadValue<string>("jti"));
    }

    [Fact]
    public async Task Token_hop_le_voi_tham_so_validate_cua_Program_cs()
    {
        var token = Issuer().Issue(Guid.NewGuid(), "USER").Token;

        // Chép từ Program.cs (AddJwtBearer). Sửa bên đó thì sửa ở đây.
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://unit.socialapp.local",
            ValidAudience = "socialapp-api-unit",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.SigningKey)),
            ValidAlgorithms = ["HS256"],
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
