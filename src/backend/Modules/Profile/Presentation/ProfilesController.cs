using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Profile.Presentation;

/// <summary>
/// Tầng HTTP của module Profile — bốn action theo <c>profile-v1.yaml</c>: <c>GET {userId}/profile</c> (D1),
/// <c>PUT me/profile</c> (D2), <c>PUT me/avatar</c> + <c>DELETE me/avatar</c> (D3). D1 mới nối action đầu tiên.
///
/// <list type="bullet">
/// <item><b>Route KHÔNG có ràng buộc <c>:guid</c>.</b> Với <c>Guid userId</c>, id sai dạng làm model binding hỏng và
/// <c>[ApiController]</c> trả 400 kèm <c>errors.userId</c> — đúng hợp đồng. Thêm <c>{userId:guid}</c> thì route không khớp
/// nữa và ra 404: sai hợp đồng, và tệ hơn là 404 đó trùng mã với tín hiệu onboarding nên FE đọc nhầm thành "chưa có hồ sơ".
/// Hệ quả kèm theo: <c>GET /users/me/profile</c> (không có trong hợp đồng) rơi vào <c>{userId}</c> → 400, không phải một
/// endpoint ẩn.</item>
/// <item><b>Không <c>[RequirePermission]</c>.</b> Hồ sơ công khai trong MVP (Mục 6.1) — chỉ cần <c>[Authorize]</c>. Không
/// mã nào trong 17 quyền nghĩa là "đọc hồ sơ người khác" và bịa thêm quyền là sửa ma trận (Đ-2.6).</item>
/// <item><b>Không <c>[Produces("application/json")]</c> ở class.</b> Bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi, và ở đây nhánh lỗi 404 chính là thứ FE dựa vào.</item>
/// <item><b>Không <c>[EnableRateLimiting("auth")]</c>:</b> khối D dùng hạn mức chung 100 req/phút theo user.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
[ApiExplorerSettings(GroupName = ProfileApiGroup.Name)]
public sealed class ProfilesController(ProfileService profiles) : ControllerBase
{
    /// <summary>
    /// Hồ sơ công khai của một người dùng. 404 = chưa onboarding (Đ-2.4) hoặc không tồn tại — cùng một phản hồi, và
    /// <c>detail</c> không nêu <paramref name="userId"/> (PROF-03).
    /// </summary>
    [HttpGet("{userId}/profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ProfileResponse>> Get(Guid userId, CancellationToken ct)
    {
        var result = await profiles.GetAsync(userId, ct);
        return result.ToActionResult(this);
    }
}
