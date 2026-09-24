using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Reactions;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Content.Presentation;

/// <summary>
/// Cảm xúc "của tôi trên đối tượng này" (GĐ3, UC-07, FR-008, Đ-3.7). <c>me</c> thay cho id người dùng — không có đường nào gửi
/// <c>userId</c> của người khác; đối tượng nằm trên route để tầng 3, log và rate limit đều thấy nó. Controller mỏng: mọi luật ở
/// <see cref="ReactionService"/>.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = ContentApiGroup.Name)]
public sealed class ReactionsController(ReactionService reactions) : ControllerBase
{
    /// <summary>D5 — thả / đổi cảm xúc trên bài. Idempotent: cùng <c>type</c> hai lần là một dòng.</summary>
    [HttpPut("posts/{postId}/reactions/me")]
    [RequirePermission(ContentPermissions.ReactionSet)]
    [ProducesResponseType<ReactionSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ReactionSummary>> SetOnPost(Guid postId, SetReactionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await reactions.ApplyToPostAsync(postId, User.GetUserId(), request.Type, ct);
        return result.ToActionResult(this);
    }

    /// <summary>D5 — gỡ cảm xúc trên bài. Chưa thả gì vẫn 200 với tóm tắt hiện tại (Đ-3.7).</summary>
    [HttpDelete("posts/{postId}/reactions/me")]
    [RequirePermission(ContentPermissions.ReactionSet)]
    [ProducesResponseType<ReactionSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ReactionSummary>> ClearOnPost(Guid postId, CancellationToken ct)
    {
        var result = await reactions.ApplyToPostAsync(postId, User.GetUserId(), desired: null, ct);
        return result.ToActionResult(this);
    }

    /// <summary>D6 — thả / đổi cảm xúc trên bình luận. Bình luận đã xóa → 404.</summary>
    [HttpPut("comments/{commentId}/reactions/me")]
    [RequirePermission(ContentPermissions.ReactionSet)]
    [ProducesResponseType<ReactionSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ReactionSummary>> SetOnComment(
        Guid commentId, SetReactionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await reactions.ApplyToCommentAsync(commentId, User.GetUserId(), request.Type, ct);
        return result.ToActionResult(this);
    }

    /// <summary>D6 — gỡ cảm xúc trên bình luận.</summary>
    [HttpDelete("comments/{commentId}/reactions/me")]
    [RequirePermission(ContentPermissions.ReactionSet)]
    [ProducesResponseType<ReactionSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ReactionSummary>> ClearOnComment(Guid commentId, CancellationToken ct)
    {
        var result = await reactions.ApplyToCommentAsync(commentId, User.GetUserId(), desired: null, ct);
        return result.ToActionResult(this);
    }
}
