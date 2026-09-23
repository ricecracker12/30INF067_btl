using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SocialApp.SharedKernel.Moderation;

public static class ModerationTargetsExtensions
{
    /// <summary>
    /// Composite <see cref="IModerationTargets"/> (C2 GĐ6). <c>AddSharedKernel</c> gọi hàm này; test dựng container trần gọi thẳng.
    /// Scoped vì provider giữ <c>DbContext</c> của module chủ. Provider do từng module đăng ký — hàm này không biết module nào.
    /// </summary>
    public static IServiceCollection AddModerationTargets(this IServiceCollection services)
    {
        services.TryAddScoped<IModerationTargets, ModerationTargets>();
        return services;
    }
}
