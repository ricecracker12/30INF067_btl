using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.DependencyInjection;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Moderation.Presentation;

/// <summary>
/// <c>POST /reports</c> (FR-019, Đ-6.12) — endpoint duy nhất của <c>moderation-v1</c> KHÔNG đặc quyền (B.10 #8, cố ý): người dùng
/// thường gửi báo cáo. <c>[PrivilegedEndpoint]</c> ở đây là fail-closed khi Redis chết (chặn người dùng vì hạ tầng) và một dòng
/// <c>access.denied</c> mỗi lần một vai trò thiếu <c>report.create</c> gọi (ngập bảng audit). <c>PrivilegedEndpointTests</c> nhận
/// diện ngoại lệ này bằng method + đường (<c>POST …/reports</c>).
///
/// Đó là lý do DUY NHẤT tách controller (cạm bẫy 3 Mục 2): hàng đợi, chi tiết, quyết định (D7) cùng đường <c>/reports</c> nhưng ở
/// <c>ReportsController</c> mang <c>[PrivilegedEndpoint]</c> ở class.
///
/// Không <c>[Produces("application/json")]</c> ở class — bài học <c>MeController</c>: nó đè <c>application/problem+json</c>.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[ApiExplorerSettings(GroupName = ModerationApiGroup.Name)]
public sealed class ReportSubmissionController(ReportSubmissionService reports) : ControllerBase
{
    /// <summary>
    /// Báo cáo một bài, bình luận hoặc tài khoản. 201 báo cáo mới; 200 kèm CHÍNH báo cáo mở đã có khi người này báo lại cùng đối
    /// tượng (bấm hai lần, hai tab). 201 không kèm <c>Location</c>: người báo không có <c>GET</c> nào để trỏ tới.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModerationPermissions.ReportCreate)]
    [EnableRateLimiting(SharedKernelExtensions.ReportCreateRateLimitPolicy)]
    [ProducesResponseType<ReportReceipt>(StatusCodes.Status201Created)]
    [ProducesResponseType<ReportReceipt>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ReportReceipt>> Create(CreateReportRequest request, CancellationToken ct)
    {
        var result = await reports.SubmitAsync(User.GetUserId(), request, ct);
        if (result.IsFailure)
            return result.Error!.Value.ToActionResult(this);

        var (receipt, created) = result.Value;
        return created ? StatusCode(StatusCodes.Status201Created, receipt) : Ok(receipt);
    }
}
