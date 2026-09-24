using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Profile.Application.Search;

namespace SocialApp.Modules.Profile.Presentation;

/// <summary>
/// Tìm người theo tên (GĐ6 D12, Đ-6.19, UC-16). Ở Profile vì dữ liệu là hồ sơ; nhóm <c>profile-v1</c> (mở lại chỉ-thêm, Mục 8.5).
///
/// <c>[Authorize]</c> không mã quyền — cùng lý do xem hồ sơ (Mục 6.1). Không <c>[PrivilegedEndpoint]</c>, không rate limit riêng: hạn
/// mức chung 100 req/phút theo user đủ cho ô gõ có debounce phía FE.
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize]
[ApiExplorerSettings(GroupName = ProfileApiGroup.Name)]
public sealed class SearchController(SearchService search) : ControllerBase
{
    /// <summary>Tối đa <c>limit</c> người có tên khớp tiền tố của từ khóa (không dấu, không phân biệt hoa thường), tốt nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<SearchPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<SearchPage>> Search([FromQuery] SearchUsersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await search.SearchAsync(query, ct));
    }
}
