using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Comments;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Content.Presentation;

/// <summary>
/// Bình luận 3 cấp (GĐ3, UC-06, FR-007). Cùng nhóm Swagger <c>content-v1</c> (Đ-3.1): nhóm bám theo MODULE. <c>actorId</c> luôn từ
/// <c>User.GetUserId()</c>, không từ route/body (B.9 mục 1). Route không ràng buộc <c>:guid</c> — id sai dạng là 400
/// <c>errors.&lt;tên&gt;</c> từ model binding, không phải 404 (Q-D7 của GĐ2).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = ContentApiGroup.Name)]
public sealed class CommentsController(CommentService comments, CommentReadService reads) : ControllerBase
{
    /// <summary>D1 — bình luận gốc của bài, cũ trước. Bài không xem được → 404 (<c>READ-CMT-01</c>).</summary>
    [HttpGet("posts/{postId}/comments")]
    [RequirePermission(ContentPermissions.PostReadPublic)]
    [ProducesResponseType<CommentPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CommentPage>> ListForPost(
        Guid postId, [FromQuery] CommentPageQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await reads.ListForPostAsync(postId, User.GetUserId(), query.Cursor, query.EffectiveLimit, ct);
        return result.ToActionResult(this);
    }

    /// <summary>D2 — phản hồi trực tiếp của một bình luận. Lần về bài rồi hỏi BR-02 (<c>READ-CMT-03</c>).</summary>
    [HttpGet("comments/{commentId}/replies")]
    [RequirePermission(ContentPermissions.PostReadPublic)]
    [ProducesResponseType<CommentPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CommentPage>> ListReplies(
        Guid commentId, [FromQuery] CommentPageQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await reads.ListRepliesAsync(commentId, User.GetUserId(), query.Cursor, query.EffectiveLimit, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// D3 — bình luận hoặc trả lời. Thứ tự kiểm (hồ sơ → BR-02 → cha) nằm ở <see cref="CommentService.CreateAsync"/> và là một
    /// phần của hợp đồng.
    /// </summary>
    [HttpPost("posts/{postId}/comments")]
    [RequirePermission(ContentPermissions.CommentCreate)]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CommentResponse>> Create(
        Guid postId, CreateCommentRequest request, CancellationToken ct)
    {
        var result = await comments.CreateAsync(postId, User.GetUserId(), request, ct);

        // Cùng lý do PostsController.Create: không CreatedAtAction — header Location không nằm trong hợp đồng.
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.ToActionResult(this);
    }

    /// <summary>
    /// D4 — xóa mềm bình luận của mình. Tầng 2 chỉ <c>[Authorize]</c> (Đ-3.2): xóa dữ liệu của mình là quyền của chủ dữ liệu —
    /// người bị gỡ <c>comment.create</c> vẫn phải xóa được bình luận cũ. Tiền lệ: <c>DELETE /users/me/avatar</c>.
    /// </summary>
    [HttpDelete("comments/{commentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid commentId, CancellationToken ct)
    {
        var result = await comments.DeleteAsync(commentId, User.GetUserId(), ct);
        return result.ToActionResult(this);
    }
}
