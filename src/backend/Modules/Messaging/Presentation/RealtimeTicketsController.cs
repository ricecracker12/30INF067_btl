using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialApp.Modules.Messaging.Application;
using SocialApp.Modules.Messaging.Application.Realtime;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Http;
using SocialApp.SharedKernel.Realtime;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Endpoint xin vé realtime (Đ-E16, Đ-5.9). Nằm ở Messaging vì Đ-E16 đòi endpoint vé thuộc một file hợp đồng; kho vé và scheme
/// ở SharedKernel để hub của module khác (GĐ6) dùng lại.
///
/// Trình duyệt gọi qua proxy BFF chung <c>/bff/api/realtime/tickets</c> — bearer của phiên gắn ở Next server, không route BFF
/// mới. <c>[Authorize]</c> trần: ai đã đăng nhập cũng mở được kênh realtime; quyền của từng thao tác kiểm ở hub (Đ-5.7).
/// </summary>
[ApiController]
[Route("api/v1/realtime")]
[Authorize]
[ApiExplorerSettings(GroupName = MessagingApiGroup.Name)]
public sealed class RealtimeTicketsController(IRealtimeTicketStore tickets) : ControllerBase
{
    /// <summary>
    /// Cấp một vé 30 giây dùng một lần. Danh tính lấy từ access token của CHÍNH request này (<c>sub</c>, <c>role</c>, <c>iat</c>) —
    /// <c>iat</c> để <c>revoked:user</c> sau này cắt được kết nối mở bằng vé (Đ-5.10). Redis chết → 503, không fail-open.
    /// </summary>
    [HttpPost("tickets")]
    [EnableRateLimiting(RealtimeTicketDefaults.RateLimitPolicy)]
    [ProducesResponseType<RealtimeTicketResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RealtimeTicketResponse>> Issue(CancellationToken ct)
    {
        // Tầng 1 đã bảo đảm sub/iat có mặt (OnTokenValidated từ chối token thiếu) — role luôn có trong access token của GĐ1.
        var claims = new RealtimeTicketClaims(
            User.FindFirstValue(JwtClaims.Sub)!,
            User.FindFirstValue(JwtClaims.Role) ?? "",
            long.Parse(User.FindFirstValue(JwtClaims.Iat)!, CultureInfo.InvariantCulture));

        var issue = await tickets.IssueAsync(claims, ct);
        if (issue is null)
            return MessagingErrors.RealtimeUnavailable.ToActionResult(this);

        return StatusCode(StatusCodes.Status201Created, new RealtimeTicketResponse(issue.Ticket, issue.ExpiresIn));
    }
}
