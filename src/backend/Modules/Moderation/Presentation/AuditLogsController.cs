using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Audit;
using SocialApp.SharedKernel.Authorization;

namespace SocialApp.Modules.Moderation.Presentation;

/// <summary>
/// Nhật ký kiểm toán cho Admin (GĐ6 D8, ENT-13, PTTK Mục 5.7). Ở module <b>Moderation</b> dù đường là <c>/admin/…</c>: dữ liệu là của
/// Moderation (Mục 8.1 xếp vào <c>moderation-v1</c>). <c>[PrivilegedEndpoint]</c> ở class: fail-closed + audit lần bị từ chối.
///
/// <c>audit.read</c> chỉ ADMIN có (ma trận PTTK). "Moderator xem lịch sử" là <c>history</c> của <c>GET /reports/{id}</c> (D7b), không phải
/// endpoint này (cạm bẫy 2, Mục 13 #9).
/// </summary>
[ApiController]
[Route("api/v1/admin/audit-logs")]
[PrivilegedEndpoint]
[ApiExplorerSettings(GroupName = ModerationApiGroup.Name)]
public sealed class AuditLogsController(AuditLogReadService audit) : ControllerBase
{
    /// <summary>
    /// Nhật ký mới nhất trước (<c>id</c> giảm dần), lọc tùy chọn theo người thao tác, hành động, đối tượng. <c>targetId</c> phải đi kèm
    /// <c>targetType</c>; <c>action</c> đứng một mình được (L-D14).
    /// </summary>
    [HttpGet]
    [RequirePermission(ModerationPermissions.AuditRead)]
    [ProducesResponseType<AuditLogPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AuditLogPage>> List([FromQuery] ListAuditLogsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await audit.ListAsync(query, ct));
    }
}
