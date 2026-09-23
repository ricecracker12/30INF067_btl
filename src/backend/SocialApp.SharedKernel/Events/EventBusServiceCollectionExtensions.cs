using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.SharedKernel.Redis;

namespace SocialApp.SharedKernel.Events;

/// <summary>Ráp event bus trong tiến trình (Đ-6.2) và đăng ký handler.</summary>
public static class EventBusServiceCollectionExtensions
{
    /// <summary>
    /// Host gọi qua <c>AddSharedKernel()</c> — <c>Program.cs</c> không có dòng riêng. Public để test dựng trần bằng
    /// <c>new ServiceCollection()</c> gọi được.
    ///
    /// <b>MỘT instance</b> cho cả <see cref="IEventPublisher"/> lẫn <c>IHostedService</c>: <c>AddHostedService&lt;InProcessEventBus&gt;()</c>
    /// dựng instance thứ hai — người phát ghi vào hàng đợi của instance này, <c>BackgroundService</c> đọc hàng đợi của instance
    /// kia, không handler nào chạy và không có lỗi nào (EVT-05).
    /// </summary>
    public static IServiceCollection AddInProcessEventBus(this IServiceCollection services)
    {
        services.AddMetrics();   // IMeterFactory — TryAdd bên trong, gọi lại không sao
        // Cùng nếp TryAdd với AddSharedKernelRedis: một đồng hồ, một throttle cho cả process.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FailOpenLogThrottle>();
        services.AddOptions<EventBusOptions>();
        services.TryAddSingleton<InProcessEventBus>();
        services.TryAddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InProcessEventBus>());
        services.AddHostedService(sp => sp.GetRequiredService<InProcessEventBus>());
        return services;
    }

    /// <summary>
    /// Cách DUY NHẤT để đăng ký handler — gọi trong <c>Add&lt;X&gt;Module</c> của module tiêu thụ. Handler vào DI như kiểu cụ thể
    /// (scoped, mỗi lần chạy một scope riêng); đăng ký trùng cùng cặp event + handler làm bus ném lúc dựng.
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.TryAddScoped<THandler>();
        services.AddSingleton(new EventHandlerRegistration(
            typeof(TEvent),
            typeof(THandler),
            static (handler, e, ct) => ((THandler)handler).HandleAsync((TEvent)e, ct)));
        return services;
    }
}
