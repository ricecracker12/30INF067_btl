using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Notification.Application;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Notification.Presentation;

/// <summary>
/// Tầng HTTP của thông báo (GĐ6 D11, Mục 8.3). Vỏ mỏng: người nhận luôn là người gọi (<c>User.GetUserId()</c>, luật 2 Mục 1.3), gọi
/// service, dịch <c>Result</c> sang RFC 7807.
///
/// <c>[Authorize]</c> không mã quyền (Mục 6.1, cùng lý do <c>/me</c>). Không <c>[PrivilegedEndpoint]</c>: chuông không được tắt khi Redis
/// chết. Route không ràng buộc <c>:guid</c>: id sai dạng → 400 <c>errors.notificationId</c>, khớp yaml. Không
/// <c>[Produces("application/json")]</c> ở class — nó đè <c>application/problem+json</c> (bài học <c>MeController</c> GĐ1).
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[ApiExplorerSettings(GroupName = NotificationApiGroup.Name)]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    /// <summary>Nhóm thông báo của người gọi, nhóm vừa có sự kiện mới lên đầu (<c>updated_at DESC, id DESC</c>).</summary>
    [HttpGet]
    [ProducesResponseType<NotificationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<NotificationPage>> List([FromQuery] ListNotificationsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await notifications.ListAsync(User.GetUserId(), query, ct));
    }

    /// <summary>Số NHÓM chưa đọc — FE hỏi lại mỗi 30 giây khi chưa có hub (Đ-6.18). Route chữ cố định ưu tiên hơn <c>{notificationId}</c>.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadNotificationCount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<UnreadNotificationCount>> UnreadCount(CancellationToken ct) =>
        Ok(await notifications.CountUnreadAsync(User.GetUserId(), ct));

    /// <summary>Đánh dấu một nhóm đã đọc. Không tồn tại hoặc của người khác → 403 CÙNG thân lỗi (<c>NOTIF-IDOR</c>). Đã đọc rồi → 204.</summary>
    [HttpPost("{notificationId}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Read(Guid notificationId, CancellationToken ct) =>
        (await notifications.MarkReadAsync(User.GetUserId(), notificationId, ct)).ToActionResult(this);

    /// <summary>Đánh dấu đã đọc mọi nhóm có <c>updatedAt ≤ upTo</c> — nhóm tới sau lúc mở chuông vẫn chưa đọc (<c>NOTIF-07</c>).</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ReadAll(ReadAllRequest request, CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(User.GetUserId(), request, ct);
        return NoContent();
    }
}
