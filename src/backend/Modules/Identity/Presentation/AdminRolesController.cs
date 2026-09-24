using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Identity.Application.Roles;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Vai trò và ma trận quyền (GĐ6 D5, Đ-6.9, nhóm <see cref="AdminApiGroup"/>). Mọi action đòi <c>role.manage</c> — mã chỉ ADMIN có
/// (L-D19: API không gán nó cho vai trò nào). <c>[PrivilegedEndpoint]</c> ở class: fail-closed + audit khi bị từ chối (◆ Mục 6.1).
///
/// <c>[RequirePermission]</c> ở class, không từng action: cả năm action cùng MỘT mã, action thêm sau không thể quên (khác
/// <see cref="AdminUsersController"/>, nơi mỗi action một mã). Route <c>{roleId}</c> không ràng buộc kiểu — sai dạng → 400
/// <c>errors.roleId</c>, khớp yaml.
/// </summary>
[ApiController]
[Route("api/v1/admin/roles")]
[PrivilegedEndpoint]
[RequirePermission(PermissionCodes.RoleManage)]
[ApiExplorerSettings(GroupName = AdminApiGroup.Name)]
public sealed class AdminRolesController(RoleAdministrationService roles) : ControllerBase
{
    /// <summary>Mọi vai trò, kèm quyền hiệu lực và số người mang.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<RoleSummary>>> List(CancellationToken ct) => Ok(await roles.ListAsync(ct));

    /// <summary>Tạo vai trò. 201 kèm <c>RoleSummary</c>, không header <c>Location</c> (không có trong hợp đồng).</summary>
    [HttpPost]
    [ProducesResponseType<RoleSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RoleSummary>> Create(CreateRoleRequest request, CancellationToken ct)
    {
        var result = await roles.CreateAsync(User.GetUserId(), request, ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.ToActionResult(this);
    }

    /// <summary>Đổi tên hiển thị — CHỈ <c>displayName</c>; body có <c>code</c> → 400 (Đ-6.9).</summary>
    [HttpPatch("{roleId}")]
    [ProducesResponseType<RoleSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RoleSummary>> Rename(short roleId, RenameRoleRequest request, CancellationToken ct) =>
        (await roles.RenameAsync(roleId, User.GetUserId(), request, ct)).ToActionResult(this);

    /// <summary>Thay cả tập quyền. USER/MODERATOR cần <c>confirm: true</c> (409 mang diff); ADMIN → 409.</summary>
    [HttpPut("{roleId}/permissions")]
    [ProducesResponseType<RoleSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RoleSummary>> SetPermissions(
        short roleId, SetRolePermissionsRequest request, CancellationToken ct) =>
        (await roles.SetPermissionsAsync(roleId, User.GetUserId(), request, ct)).ToActionResult(this);

    /// <summary>Xóa vai trò tự tạo không còn ai mang. 204.</summary>
    [HttpDelete("{roleId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Delete(short roleId, CancellationToken ct) =>
        (await roles.DeleteAsync(roleId, User.GetUserId(), ct)).ToActionResult(this);
}

/// <summary>Danh mục 18 mã quyền cho ma trận của màn vai trò (D5). Cùng tầng 2 với <see cref="AdminRolesController"/>.</summary>
[ApiController]
[Route("api/v1/admin/permissions")]
[PrivilegedEndpoint]
[RequirePermission(PermissionCodes.RoleManage)]
[ApiExplorerSettings(GroupName = AdminApiGroup.Name)]
public sealed class AdminPermissionsController(RoleAdministrationService roles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PermissionInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<PermissionInfo>>> List(CancellationToken ct) =>
        Ok(await roles.ListPermissionsAsync(ct));
}
