using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.SocialGraph.Application.Relationships;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.SocialGraph.Presentation;

/// <summary>
/// Tầng HTTP của <c>GET /relationships/{userId}</c> — nút trên trang hồ sơ (Đ-4.16). D2–D6 thêm
/// <c>FriendsController</c> / <c>FollowsController</c>; cả ba cùng nhóm Swagger
/// <see cref="SocialGraphApiGroup"/> (nhóm bám theo MODULE, không theo controller).
///
/// <list type="bullet">
/// <item><b>Route KHÔNG có ràng buộc <c>:guid</c>.</b> Với <c>Guid userId</c>, id sai dạng làm model binding hỏng
/// và <c>[ApiController]</c> trả 400 kèm <c>errors.userId</c> — đúng hợp đồng. Thêm <c>{userId:guid}</c> thì
/// route không khớp và ra 404, lệch yaml.</item>
/// <item><b>Không <c>[RequirePermission]</c>.</b> Đọc quan hệ của chính người gọi (Mục 6.3) — chỉ
/// <c>[Authorize]</c>. Không mã nào trong ma trận nghĩa là "xem trạng thái nút".</item>
/// <item><b>Không <c>[Produces("application/json")]</c> ở class</b> — bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = SocialGraphApiGroup.Name)]
public sealed class RelationshipsController(RelationshipService relationships) : ControllerBase
{
    /// <summary>
    /// Quan hệ giữa người gọi và <paramref name="userId"/>. <c>actorId</c> từ <c>User.GetUserId()</c>, không từ
    /// route (Mục 1.3 luật 5). Không có 404 trong danh sách mã: người không tồn tại là 200 <c>none</c>/<c>false</c>.
    /// </summary>
    [HttpGet("relationships/{userId}")]
    [ProducesResponseType<RelationshipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<RelationshipResponse>> Get(Guid userId, CancellationToken ct)
    {
        var result = await relationships.GetAsync(User.GetUserId(), userId, ct);
        return result.ToActionResult(this);
    }
}
