using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.SocialGraph.Application;
using SocialApp.Modules.SocialGraph.Application.Relationships;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.SocialGraph.Presentation;

/// <summary>
/// Tầng HTTP của theo dõi một chiều (UC-13, FR-012). Cùng nhóm Swagger <see cref="SocialGraphApiGroup"/>
/// với <c>FriendsController</c> — nhóm bám theo MODULE, không theo controller.
///
/// Không <c>[Produces("application/json")]</c> ở class — bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = SocialGraphApiGroup.Name)]
public sealed class FollowsController(RelationshipService relationships) : ControllerBase
{
    /// <summary>
    /// Theo dõi <paramref name="userId"/> (FR-012). Idempotent: đã theo dõi rồi vẫn 204 — vì vậy là
    /// <c>PUT</c>, không phải <c>POST</c>.
    ///
    /// <c>[RequirePermission]</c> cùng mã <c>friend.request</c> (Đ-4.12): chủ động tạo một kết nối xã hội.
    /// <c>actorId</c> từ <c>User.GetUserId()</c>, không từ route.
    /// Route không ràng buộc <c>:guid</c>: id sai dạng → 400 <c>errors.userId</c>, khớp yaml.
    /// </summary>
    [HttpPut("follows/{userId}")]
    [RequirePermission(SocialGraphPermissions.FriendRequest)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Follow(Guid userId, CancellationToken ct)
    {
        var result = await relationships.FollowAsync(User.GetUserId(), userId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Bỏ theo dõi (FR-012). <c>[Authorize]</c> trần — không <c>[RequirePermission]</c> (Đ-4.12).
    /// 0 dòng vẫn 204. Quan hệ bạn bè không bị xóa ở đây.
    /// </summary>
    [HttpDelete("follows/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Unfollow(Guid userId, CancellationToken ct)
    {
        var result = await relationships.UnfollowAsync(User.GetUserId(), userId, ct);
        return result.ToActionResult(this);
    }
}
