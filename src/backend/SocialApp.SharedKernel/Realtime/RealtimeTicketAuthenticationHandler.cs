using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Scheme <see cref="RealtimeTicketDefaults.Scheme"/> (Đ-E16, Đ-5.9): bắt tay hub bằng vé dùng một lần trên query string.
///
/// Hai chiều canh nhau:
/// - Scheme này CHỈ đọc <c>?access_token=</c> khi đường bắt đầu bằng <c>/hubs</c>, và REST không khai scheme này — vé đặt vào
///   <c>Authorization</c> của REST là 401 (HUB-06).
/// - Hub KHÔNG nhận bearer: JWT đặt vào <c>?access_token=</c> không phải một vé có trong Redis → 401 (HUB-05). Nhờ vậy JWT 15 phút
///   không bao giờ nằm trong log truy cập — đúng thứ Đ-E16 sinh ra để tránh.
///
/// Sau khi đổi vé: kiểm <c>revoked:user</c> với <c>iat</c> của access token lúc xin vé (HUB-04). Redis không trả lời được thì
/// cho qua như REST của GĐ1 (fail-open) — nhưng chính vé cũng nằm trong Redis, nên Redis chết thì đã không có vé nào đổi được.
/// </summary>
public sealed class RealtimeTicketAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IRealtimeTicketStore tickets,
    ITokenRevocationStore revocation)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments(RealtimeTicketDefaults.HubsPathPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var ticket = Request.Query[RealtimeTicketDefaults.QueryKey].ToString();
        if (string.IsNullOrEmpty(ticket))
            return AuthenticateResult.NoResult();

        var claims = await tickets.RedeemAsync(ticket, Context.RequestAborted);
        if (claims is null)
            return AuthenticateResult.Fail("vé realtime không hợp lệ");   // không kèm vé, không kèm lý do chi tiết

        if (await revocation.CheckAsync(claims.Sub, claims.Iat, Context.RequestAborted) == RevocationCheck.Revoked)
            return AuthenticateResult.Fail("phiên đã bị thu hồi");

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtClaims.Sub, claims.Sub),
                new Claim(JwtClaims.Role, claims.Role),
                new Claim(JwtClaims.Iat, claims.Iat.ToString(CultureInfo.InvariantCulture)),
            ],
            RealtimeTicketDefaults.Scheme,
            JwtClaims.Sub,
            JwtClaims.Role);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), RealtimeTicketDefaults.Scheme));
    }
}
