using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Targets;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Moderation.Presentation;

/// <summary>
/// Thao tác trên ĐỐI TƯỢNG kiểm duyệt, không qua báo cáo (GĐ6 D7c, FR-020 "ẩn/khôi phục"). <c>[PrivilegedEndpoint]</c> ở class như mọi
/// controller của <c>moderation-v1</c> trừ <c>POST /reports</c> (B.10 #8).
/// </summary>
[ApiController]
[Route("api/v1/moderation/targets")]
[PrivilegedEndpoint]
[ApiExplorerSettings(GroupName = ModerationApiGroup.Name)]
public sealed class ModerationTargetsController(RestoreTargetService restores) : ControllerBase
{
    /// <summary>
    /// Khôi phục bài/bình luận bị ẩn: <c>hidden → published</c> + audit <c>content.restore</c> trong một transaction. Không mở lại báo
    /// cáo đã đóng, không thông báo ai. <c>targetType: user</c> → 400 (người dùng không "ẩn" được); đang không ẩn → 409
    /// <c>moderation-not-hidden</c>. Route không ràng buộc <c>:guid</c> — id sai dạng → 400 <c>errors.targetId</c>, như mọi route khác của
    /// khối D. Body tùy chọn: gọi không body là không ghi chú.
    /// </summary>
    [HttpPost("{targetType}/{targetId}/restore")]
    [RequirePermission(ModerationPermissions.PostHide)]
    [ProducesResponseType<ModerationTargetChange>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ModerationTargetChange>> Restore(
        string targetType,
        Guid targetId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RestoreTargetRequest? request,
        CancellationToken ct)
    {
        var result = await restores.RestoreAsync(targetType, targetId, User.GetUserId(), request, ct);
        return result.ToActionResult(this);
    }
}
