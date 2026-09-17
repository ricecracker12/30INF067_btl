using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Identity.Application.Me;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// <c>GET /me</c> — smoke test rẻ nhất chứng minh token phát ở login đi qua tầng 1 và chạm tới DB. Giữ qua mọi giai đoạn.
///
/// <list type="bullet">
/// <item><b>Không <c>[RequirePermission]</c>.</b> Không mã nào trong 17 quyền của Mục 5.2 nghĩa là "đọc chính mình", và bịa
/// thêm quyền là sửa ma trận. Tầng 2 đã được AuthZ matrix chứng minh trên probe (<c>RBAC-*</c>); <c>/me</c> chứng minh tầng
/// 1 + dữ liệu. GĐ sau đừng "sửa" bằng cách gắn một quyền ngẫu nhiên.</item>
/// <item><b>Không <c>[EnableRateLimiting("auth")]</c>:</b> <c>/me</c> dùng hạn mức chung 100 req/phút theo user.</item>
/// <item><b>Không <c>[Produces("application/json")]</c>:</b> nhánh 401 "user đã bị xóa" đi qua <c>Problem()</c> sẽ mất
/// <c>application/problem+json</c>.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]
public sealed class MeController(MeQuery me) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        // Danh tính lấy từ token, không có tham số route/query nào (C6).
        var result = await me.GetAsync(User.GetUserId(), ct);
        return result.ToActionResult(this);
    }
}
