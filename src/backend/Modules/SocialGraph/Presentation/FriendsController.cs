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
/// Tầng HTTP của lời mời kết bạn và danh sách bạn (UC-10, UC-11). D2–D5: gửi, chấp nhận, hủy, danh sách.
/// Cùng nhóm Swagger <see cref="SocialGraphApiGroup"/> với <c>RelationshipsController</c> —
/// nhóm bám theo MODULE, không theo controller.
///
/// Không <c>[Produces("application/json")]</c> ở class — bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = SocialGraphApiGroup.Name)]
public sealed class FriendsController(RelationshipService relationships) : ControllerBase
{
    /// <summary>
    /// Gửi lời mời (FR-010). Thứ tự kiểm nằm ở <see cref="RelationshipService.SendRequestAsync"/>
    /// và là một phần của hợp đồng — controller chỉ lấy <c>actorId</c> từ token rồi chuyển xuống.
    ///
    /// <c>[RequirePermission]</c> chứ không <c>[Authorize]</c> trần: đây là tầng 2 của Đ-4.12.
    /// <c>actorId</c> từ <c>User.GetUserId()</c>, KHÔNG từ body (Mục 1.3 luật 1) —
    /// <see cref="CreateFriendRequest"/> cố ý không có trường nào mang id người gửi.
    /// </summary>
    [HttpPost("friends/requests")]
    [RequirePermission(SocialGraphPermissions.FriendRequest)]
    [ProducesResponseType<RelationshipResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RelationshipResponse>> SendRequest(
        CreateFriendRequest request, CancellationToken ct)
    {
        var result = await relationships.SendRequestAsync(User.GetUserId(), request, ct);

        // ToActionResult trả 200 khi thành công — gửi lời mời là 201. Không CreatedAtAction: header
        // Location không nằm trong hợp đồng.
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.ToActionResult(this);
    }

    /// <summary>
    /// Chấp nhận lời mời mà <paramref name="userId"/> gửi tới người gọi (FR-011, Đ-4.14).
    /// Tầng 3 là một câu <c>UPDATE</c> ở store — 0 dòng thành 403, không 404.
    ///
    /// Route không ràng buộc <c>:guid</c>: id sai dạng → 400 <c>errors.userId</c>, khớp yaml.
    /// <c>actorId</c> từ token, không từ route.
    /// </summary>
    [HttpPost("friends/requests/{userId}/accept")]
    [RequirePermission(SocialGraphPermissions.FriendRespond)]
    [ProducesResponseType<RelationshipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<RelationshipResponse>> Accept(Guid userId, CancellationToken ct)
    {
        var result = await relationships.AcceptRequestAsync(User.GetUserId(), userId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Hủy lời mình đã gửi, hoặc từ chối lời nhận được (FR-011). <c>[Authorize]</c> trần — không
    /// <c>[RequirePermission]</c> (Đ-4.12). 0 dòng vẫn 204. Quan hệ <c>accepted</c> không bị xóa ở đây.
    ///
    /// Route không ràng buộc <c>:guid</c>: id sai dạng → 400 <c>errors.userId</c>, khớp yaml.
    /// <c>actorId</c> từ token, không từ route.
    /// </summary>
    [HttpDelete("friends/requests/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> DeclineOrCancel(Guid userId, CancellationToken ct)
    {
        var result = await relationships.DeclineOrCancelAsync(User.GetUserId(), userId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Hủy kết bạn (FR-011). <c>[Authorize]</c> trần (Đ-4.12). Lời mời <c>pending</c> không bị xóa ở đây.
    /// 0 dòng vẫn 204. <c>actorId</c> từ token, không từ route.
    /// </summary>
    [HttpDelete("friends/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Unfriend(Guid userId, CancellationToken ct)
    {
        var result = await relationships.UnfriendAsync(User.GetUserId(), userId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Bạn của chính người gọi, mới kết bạn trước. <c>[Authorize]</c> trần (Đ-4.12).
    /// <paramref name="query"/> là <c>[FromQuery]</c> để FluentValidation bắt cursor rác và limit ngoài <c>1..50</c>.
    /// </summary>
    [HttpGet("friends")]
    [ProducesResponseType<FriendPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<FriendPage>> List([FromQuery] ListFriendsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await relationships.ListFriendsAsync(
            User.GetUserId(), query.Cursor, query.EffectiveLimit, ct);
        return Ok(page);
    }

    /// <summary>
    /// Lời mời <c>pending</c> của chính người gọi. Không gửi <c>direction</c> thì là incoming.
    /// Giá trị lạ thành 400 <c>errors.direction</c> ở validator — không bind enum.
    /// </summary>
    [HttpGet("friends/requests")]
    [ProducesResponseType<FriendRequestPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<FriendRequestPage>> ListRequests(
        [FromQuery] ListFriendRequestsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var incoming = query.Direction is not ListFriendRequestsQuery.Outgoing;
        var page = await relationships.ListRequestsAsync(
            User.GetUserId(), incoming, query.Cursor, query.EffectiveLimit, ct);
        return Ok(page);
    }
}
