using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Lấy service Identity qua CHÍNH <c>AddIdentityModule</c> (hiện thực là internal): test cả dòng đăng ký DI, không chỉ
/// lớp. DbContext chỉ dựng khi có người resolve — không service nào ở đây chạm DB, nên chuỗi kết nối là giả.
/// </summary>
internal static class IdentityServices
{
    public static ServiceProvider Build(JwtOptions? jwt = null, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        if (time is not null)
            services.AddSingleton(time);   // đăng ký TRƯỚC: AddIdentityModule chỉ TryAdd TimeProvider.System
        services.AddSingleton(Options.Create(jwt ?? new JwtOptions()));

        return services
            .AddIdentityModule("Host=unit-test-khong-cham-db")
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
