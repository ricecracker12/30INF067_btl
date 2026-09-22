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
/// Tầng HTTP của lời mời kết bạn và danh sách bạn (UC-10, UC-11). D2 chỉ có
/// <c>POST /friends/requests</c>; D3–D5 thêm accept / xóa / danh sách trên cùng controller.
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
}
