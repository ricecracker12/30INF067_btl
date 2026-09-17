using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Tầng 2 (Mục 6.2): xét <see cref="PermissionRequirement"/> theo claim role + ma trận quyền trong
/// <see cref="IPermissionCache"/>. ADMIN đi lối tắt — CHỈ Ở ĐÂY. Cùng dòng if đó mà nằm ở tầng 3 (kiểm tra
/// ownership) thì nghĩa là "Admin thao tác được trên tài nguyên của bất kỳ ai" — đúng lỗ IDOR GOAL-03 muốn đóng.
/// </summary>
public sealed class PermissionHandler(IPermissionCache cache) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var role = ctx.User.FindFirstValue(JwtClaims.Role);   // luôn là CHUỖI, với mọi vai trò (Mục 3.1)
        if (role is null)
            return;

        // Short-circuit Admin — CHỈ Ở ĐÂY, tầng 2. Tuyệt đối không lặp lại ở kiểm tra ownership (Mục 3.2).
        // So khớp phân biệt hoa thường: token mang đúng chuỗi hợp đồng, "admin" không phải ADMIN.
        if (role == SystemRoles.Admin)
        {
            ctx.Succeed(req);
            return;
        }

        var granted = await cache.GetAsync(role);
        if (granted.Contains(req.Permission))
            ctx.Succeed(req);

        // KHÔNG gọi ctx.Fail(): để mặc định deny, tránh chặn nhầm handler khác cùng requirement.
    }
}
