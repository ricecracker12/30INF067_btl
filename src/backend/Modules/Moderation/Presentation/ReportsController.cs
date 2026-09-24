using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Moderation.Presentation;

/// <summary>
/// Hàng đợi, chi tiết (GĐ6 D7b) và quyết định (D7c) báo cáo cho Moderator (UC-19).
///
/// <list type="bullet">
/// <item><b><c>[PrivilegedEndpoint]</c> ở mức class</b> (Mục 6.1): fail-closed khi không kiểm được thu hồi (Đ-6.8) + audit
/// <c>access.denied</c> khi tầng 2 từ chối (Đ-6.15). Cùng đường <c>api/v1/reports</c> với <see cref="ReportSubmissionController"/>
/// nhưng KHÁC controller — <c>POST /reports</c> là ngoại lệ không đặc quyền duy nhất (B.10 #8).</item>
/// <item><b>Tầng 2 khai TỪNG action</b>: D7c có tầng 2 kép (<c>post.hide</c> khi <c>hide</c>) ở service, nhưng tầng 2 đầu
/// (<c>report.resolve</c>) vẫn là attribute ở action — action quên khai thì matrix <c>TC-A06*</c> bắt.</item>
/// <item>Đây là đường DUY NHẤT Moderator đọc nội dung không công khai (Mục 8.1) — dòng matrix <c>TC-A06-queue</c> canh cửa vào.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[PrivilegedEndpoint]
[ApiExplorerSettings(GroupName = ModerationApiGroup.Name)]
public sealed class ReportsController(ReportReadService reports, DecideReportService decisions) : ControllerBase
{
    /// <summary>
    /// Hàng đợi: mỗi đối tượng bị báo một dòng, báo cáo mở cũ nhất trước. <paramref name="query"/> là <c>[FromQuery]</c> để
    /// FluentValidation bắt cursor rác, limit ngoài <c>1..50</c>, status khác <c>open</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModerationPermissions.ReportResolve)]
    [ProducesResponseType<ReportQueuePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ReportQueuePage>> List([FromQuery] ListReportsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await reports.ListAsync(query, ct));
    }

    /// <summary>
    /// Chi tiết một báo cáo: ảnh chụp đối tượng (kể cả bài riêng tư, đã ẩn, đã xóa), mọi báo cáo mở cùng đối tượng, lịch sử quyết
    /// định. Không có người báo. Route không ràng buộc <c>:guid</c>: id sai dạng → 400 <c>errors.reportId</c>, khớp yaml.
    /// </summary>
    [HttpGet("{reportId}")]
    [RequirePermission(ModerationPermissions.ReportResolve)]
    [ProducesResponseType<ReportDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ReportDetail>> Get(Guid reportId, CancellationToken ct)
    {
        var result = await reports.GetAsync(reportId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Quyết định một báo cáo (Đ-6.13): <c>hide</c> ẩn đối tượng + đóng MỌI báo cáo mở của nó + một dòng audit trong một transaction;
    /// <c>dismiss</c>/<c>resolve</c> chỉ đóng báo cáo. <c>hide</c> cần thêm <c>post.hide</c> — tầng 2 thứ hai ở service (L-D12), vai trò
    /// đọc từ claim của token, không từ body. Đã quyết rồi → 409 <c>report-already-decided</c>.
    /// </summary>
    [HttpPatch("{reportId}")]
    [RequirePermission(ModerationPermissions.ReportResolve)]
    [ProducesResponseType<ReportDecisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ReportDecisionResult>> Decide(
        Guid reportId, DecideReportRequest request, CancellationToken ct)
    {
        var result = await decisions.DecideAsync(
            reportId, User.GetUserId(), User.FindFirstValue(JwtClaims.Role), request, ct);
        return result.ToActionResult(this);
    }
}
