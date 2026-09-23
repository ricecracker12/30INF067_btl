using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Content.Presentation;

/// <summary>
/// <c>GET /feed</c> (D7, GĐ4) — vỏ mỏng của <see cref="FeedService"/>. Tách khỏi <see cref="PostsController"/>: một controller
/// một nhóm tài nguyên. Cùng nhóm Swagger <c>content-v1</c> — feed nằm trong hợp đồng của Content (Đ-4.2), và
/// <c>ContentContractTests</c> canh nó cùng sáu endpoint GĐ2.
///
/// Không <c>[Produces("application/json")]</c> ở class (bài học <c>MeController</c> GĐ1: nó đè <c>application/problem+json</c>
/// của nhánh lỗi).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = ContentApiGroup.Name)]
public sealed class FeedController(FeedService feed) : ControllerBase
{
    /// <summary>
    /// Bảng tin của người gọi (FR-009, UC-08, SEQ-03). <c>actorId</c> từ token — không có tham số nào mang id người dùng.
    ///
    /// <b>503 + <c>Retry-After: 5</c></b> khi truy vấn feed vượt 5s (Đ-4.10). Header gắn Ở ĐÂY vì <c>Result → Problem</c>
    /// không gắn header nào; và không ở middleware vì chỉ feed có hành vi này.
    /// </summary>
    [HttpGet("feed")]
    [RequirePermission(ContentPermissions.PostReadPublic)]
    [ProducesResponseType<FeedPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<FeedPage>> Get([FromQuery] FeedQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await feed.GetAsync(User.GetUserId(), query.Cursor, query.EffectiveLimit, ct);
        if (result.Error is { Status: StatusCodes.Status503ServiceUnavailable })
            Response.Headers.RetryAfter = "5";
        return result.ToActionResult(this);
    }
}
