using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Events;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// C0 (GĐ6) — phần "đăng ký <c>DrainAsync</c> của event bus" của B1, làm sớm vì EVT-06 cần. Mọi test chạm event chờ bằng hàm
/// này, KHÔNG bằng <c>Task.Delay</c>: nó trả về khi hàng đợi rỗng VÀ mọi handler đang chạy đã xong. Mọi khẳng định về event
/// đứng SAU lời gọi này — đứng trước là đỏ ngẫu nhiên.
/// </summary>
public static class EventBusHarness
{
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    public static Task DrainEventsAsync(this WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<InProcessEventBus>().DrainAsync(DrainTimeout);
}
