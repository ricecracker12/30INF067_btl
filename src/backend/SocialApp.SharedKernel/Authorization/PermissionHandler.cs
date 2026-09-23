using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Tầng 2 (Mục 6.2): xét <see cref="PermissionRequirement"/> theo claim role + ma trận quyền trong
/// <see cref="IPermissionCache"/>. ADMIN đi lối tắt — CHỈ trong <see cref="PermissionChecks.IsAllowedAsync"/> (tầng 2). Cùng dòng
/// if đó mà nằm ở tầng 3 (kiểm tra ownership) thì nghĩa là "Admin thao tác được trên tài nguyên của bất kỳ ai" — đúng lỗ IDOR
/// GOAL-03 muốn đóng.
/// </summary>
public sealed class PermissionHandler(IPermissionCache cache) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        // Claim role luôn là CHUỖI, với mọi vai trò (Mục 3.1). Thiếu claim → IsAllowedAsync trả false.
        if (await cache.IsAllowedAsync(ctx.User.FindFirstValue(JwtClaims.Role), req.Permission))
            ctx.Succeed(req);

        // KHÔNG gọi ctx.Fail(): để mặc định deny, tránh chặn nhầm handler khác cùng requirement.
    }
}
