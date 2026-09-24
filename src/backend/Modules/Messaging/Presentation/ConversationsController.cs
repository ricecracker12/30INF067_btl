using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Messaging.Application;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Tầng HTTP của hội thoại (D1–D6, giai-doan-5.md Mục 8.1). Vỏ mỏng: lấy <c>actorId</c> từ token, gọi service, dịch
/// <c>Result</c> sang RFC 7807 bằng <c>ToActionResult</c>. Kiểm thành viên (tầng 3) nằm ở <see cref="ConversationAccess"/>.
///
/// Không <c>[Produces("application/json")]</c> ở class — bài học <c>MeController</c> GĐ1: nó đè <c>application/problem+json</c>.
/// Route không ràng buộc <c>:guid</c>: id sai dạng → 400 <c>errors.conversationId</c>, khớp yaml.
/// </summary>
[ApiController]
[Route("api/v1/conversations")]
[Authorize]
[ApiExplorerSettings(GroupName = MessagingApiGroup.Name)]
public sealed class ConversationsController(ConversationService conversations, MessageSendService sender) : ControllerBase
{
    /// <summary>D1 — mở (get-or-create) hội thoại với một người bạn. 201 vừa tạo · 200 đã có.</summary>
    [HttpPost]
    [RequirePermission(MessagingPermissions.MessageSend)]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ConversationResponse>> Open(CreateConversationRequest request, CancellationToken ct)
    {
        var result = await conversations.OpenAsync(User.GetUserId(), request, ct);
        if (result.IsFailure)
            return result.Error!.Value.ToActionResult(this);

        var (response, created) = result.Value;
        return created ? StatusCode(StatusCodes.Status201Created, response) : Ok(response);
    }

    /// <summary>D2 — hội thoại đã có tin của người gọi, mới nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<ConversationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<ConversationPage>> List([FromQuery] ListConversationsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await conversations.ListAsync(User.GetUserId(), query.Cursor, query.EffectiveLimit, ct));
    }

    /// <summary>D3 — tổng chưa đọc cho badge. Route chữ cố định ưu tiên hơn <c>{conversationId}</c>.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<UnreadCountResponse>> Unread(CancellationToken ct) =>
        Ok(await conversations.UnreadAsync(User.GetUserId(), ct));

    /// <summary>D3 — chi tiết + <c>canSend</c> sống. Không phải thành viên / không tồn tại → 403 (TC-A04).</summary>
    [HttpGet("{conversationId}")]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<ConversationResponse>> Get(Guid conversationId, CancellationToken ct)
    {
        var result = await conversations.GetAsync(User.GetUserId(), conversationId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>D4 — lịch sử: cuộn ngược theo <c>cursor</c> hoặc lấp chỗ hở theo <c>afterSeq</c>.</summary>
    [HttpGet("{conversationId}/messages")]
    [ProducesResponseType<MessagePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<MessagePage>> History(
        Guid conversationId, [FromQuery] MessageHistoryQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await conversations.HistoryAsync(User.GetUserId(), conversationId, query, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// D5 — gửi tin bằng REST (đường fallback, Đ-5.12). 201 tin mới · 200 gửi lại cùng <c>clientMsgId</c> + cùng nội dung
    /// (ĐÚNG tin cũ, Đ-5.5) · 409 cùng <c>clientMsgId</c> khác nội dung · 403 không phải thành viên / không còn là bạn.
    /// </summary>
    [HttpPost("{conversationId}/messages")]
    [RequirePermission(MessagingPermissions.MessageSend)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<MessageResponse>> Send(
        Guid conversationId, SendMessageRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(
            User.GetUserId(), User.FindFirst(JwtClaims.Role)?.Value, conversationId, request.Content, request.ClientMsgId,
            SendChannel.Rest, ct);
        if (result.IsFailure)
            return result.Error!.Value.ToActionResult(this);

        return result.Value!.Replayed
            ? Ok(result.Value.Message)
            : StatusCode(StatusCodes.Status201Created, result.Value.Message);
    }

    /// <summary>D6 — biên nhận đã nhận/đã xem. Chỉ thành viên; hủy kết bạn vẫn gửi được (Đ-5.3). 204 kể cả khi mốc không đổi.</summary>
    [HttpPost("{conversationId}/receipts")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Receipt(Guid conversationId, ReceiptRequest request, CancellationToken ct)
    {
        var result = await conversations.ReceiptAsync(
            User.GetUserId(), conversationId, request.Kind!.Value, request.UpToSeq, ct);
        return result.ToActionResult(this);
    }
}
