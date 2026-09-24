using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Identity.Application.Admin.Users;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Màn quản trị tài khoản (GĐ6 UC-20, nhóm <see cref="AdminApiGroup"/>). D2: danh sách + chi tiết. D3, D4 thêm khóa/mở khóa và
/// đổi vai trò vào CHÍNH controller này.
///
/// <list type="bullet">
/// <item><b><c>[PrivilegedEndpoint]</c> ở mức class</b> (◆ Mục 6.1): fail-closed khi không kiểm được thu hồi (Đ-6.8) + audit
/// <c>access.denied</c> khi tầng 2 từ chối (Đ-6.15). Gắn ở class để action D3/D4 thêm sau không quên được.
/// <c>Privileged_controllers_carry_the_attribute</c> canh.</item>
/// <item><b>Tầng 2 khai TỪNG action</b>, không ở class: mỗi action một mã quyền khác nhau (danh sách any-of, khóa
/// <c>user.lock</c>, mở <c>user.unlock</c>, gán <c>role.assign</c>). Action quên khai thì fallback policy chỉ đòi đăng nhập —
/// matrix <c>TC-A05*</c> bắt.</item>
/// <item>Không <c>[Produces("application/json")]</c> ở class — bài học <c>MeController</c>: nó đè <c>application/problem+json</c>
/// của nhánh lỗi.</item>
/// <item><c>email</c> trong response là PII (Mục 8.2) — không log ở đây hay ở tầng dưới.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
[PrivilegedEndpoint]
[ApiExplorerSettings(GroupName = AdminApiGroup.Name)]
public sealed class AdminUsersController(AdminUserReadService users) : ControllerBase
{
    /// <summary>
    /// Danh sách tài khoản, mới tạo trước. Policy any-of (Mục 6.1): người chỉ có <c>role.assign</c> cũng phải xem được danh sách để
    /// chọn người gán vai trò. <paramref name="query"/> là <c>[FromQuery]</c> để FluentValidation bắt cursor rác, limit ngoài
    /// <c>1..50</c>, status/roleCode sai dạng.
    /// </summary>
    [HttpGet]
    [RequireAnyPermission(PermissionCodes.UserLock, PermissionCodes.UserUnlock, PermissionCodes.RoleAssign)]
    [ProducesResponseType<AdminUserPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AdminUserPage>> List([FromQuery] ListAdminUsersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await users.ListAsync(query, ct));
    }

    /// <summary>
    /// Một tài khoản. <paramref name="userId"/> ở đây là ĐÍCH, người thao tác là token (Mục 1.3 luật 2). Route không ràng buộc
    /// <c>:guid</c>: id sai dạng → 400 <c>errors.userId</c>, khớp yaml; id không tồn tại → 404.
    /// </summary>
    [HttpGet("{userId}")]
    [RequireAnyPermission(PermissionCodes.UserLock, PermissionCodes.UserUnlock, PermissionCodes.RoleAssign)]
    [ProducesResponseType<AdminUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AdminUser>> Get(Guid userId, CancellationToken ct)
    {
        var result = await users.GetAsync(userId, ct);
        return result.ToActionResult(this);
    }
}
