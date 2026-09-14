using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Điểm ráp DI DUY NHẤT của tầng 2. C4 thêm fallback policy, C3 thêm cache, C2 thêm handler vào cùng hàm
/// này — Api chỉ gọi một dòng.
/// </summary>
public static class AuthorizationExtensions
{
    public static IServiceCollection AddSharedKernelAuthorization(this IServiceCollection services)
    {
        // DEFAULT DENY (C4): endpoint không khai [Authorize]/[RequirePermission]/[AllowAnonymous] vẫn đòi đăng
        // nhập. Quên khai quyền thì bị chặn chứ không lọt. Thứ gì phải công khai thì khai [AllowAnonymous]
        // tường minh (healthcheck, ping, register/login...).
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

        // Provider PHẢI rơi về DefaultAuthorizationPolicyProvider cho fallback policy ở trên — xem
        // PermissionPolicyProvider. Dòng DEFAULT-DENY của AuthZ matrix bắt chuyện đó ở tầng HTTP.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        return services;
    }
}
