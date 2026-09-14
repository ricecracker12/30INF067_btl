using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // C3: cache vai trò → tập quyền (TTL 60s). Singleton, tự mở scope để gọi IRolePermissionSource (scoped,
        // Identity đăng ký ở C5). TryAdd TimeProvider để test thay được đồng hồ.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPermissionCache, PermissionCache>();

        // C2: handler của PermissionRequirement — Admin short-circuit + tra cache. Không có handler thì mọi
        // [RequirePermission] đều 403 kể cả Admin (cột "Sau C4" của bảng B3).
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        return services;
    }
}
