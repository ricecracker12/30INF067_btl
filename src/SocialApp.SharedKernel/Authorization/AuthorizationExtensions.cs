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
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        return services;
    }
}
