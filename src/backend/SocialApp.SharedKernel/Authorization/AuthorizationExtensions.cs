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

        // GĐ6 C3 (Đ-6.10): sửa quyền có hiệu lực ngay trên MỌI instance — xóa tại chỗ + pub/sub. Subscriber tự thoát khi không có
        // RedisConnection, nên không cần biết AddSharedKernelRedis đã gọi trước hay sau hàm này.
        services.AddSingleton<IPermissionChangeNotifier, PermissionChangeNotifier>();
        services.AddHostedService<PermissionsChangedSubscriber>();

        // GĐ6 C4: [RequireAnyPermission] (Mục 6.1) + [PrivilegedEndpoint] — 503 khi không kiểm được thu hồi (Đ-6.8) và audit
        // access.denied (Đ-6.15). MỘT đăng ký IAuthorizationMiddlewareResultHandler cho cả host: đăng ký thêm ở chỗ khác thì cái sau
        // thắng im lặng.
        services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
        return services;
    }
}
