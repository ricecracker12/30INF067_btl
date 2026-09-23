using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Nghe kênh <see cref="PermissionsChangedChannel"/> và xóa cache quyền của vai trò được báo (Đ-6.10) — để instance KHÁC với
/// instance xử lý request sửa quyền cũng thấy thay đổi ngay, không đợi TTL 60 giây (GĐ7 chạy hai bản sao API).
///
/// Ba chỗ dễ sai, đều đã xử lý:
/// <list type="number">
/// <item>Redis chết lúc khởi động → <c>SUBSCRIBE</c> ném. Thử lại có giãn cách, KHÔNG để ngoại lệ thoát <c>ExecuteAsync</c>: .NET 8
/// mặc định dừng host khi <c>BackgroundService</c> ném — Redis chết không được kéo sập API.</item>
/// <item>Tin phát lúc Redis rớt thì MẤT (Redis không lưu tin cho subscriber vắng mặt). Multiplexer tự đăng ký lại kênh khi nối lại,
/// nhưng không trả lại tin đã lỡ → <c>ConnectionRestored</c> gọi <see cref="IPermissionCache.InvalidateAll"/> (L-C5).</item>
/// <item>Không có <see cref="RedisConnection"/> (container trần) → thoát ngay, không làm gì.</item>
/// </list>
/// Tin do CHÍNH instance này phát cũng quay về đây — xóa lần hai vô hại.
/// </summary>
internal sealed class PermissionsChangedSubscriber(
    IServiceProvider services,
    IPermissionCache cache,
    ILogger<PermissionsChangedSubscriber> logger) : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (services.GetService<RedisConnection>() is not { } redis)
            return;

        var channel = RedisChannel.Literal(PermissionsChangedChannel.For(services));
        var delay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // WaitAsync(stoppingToken) ở CẢ HAI lượt chờ: GetAsync và SubscribeAsync không nhận token, và khi Redis không tới
                // được chúng treo tới hết timeout của thư viện. Host dừng phải chờ ExecuteAsync — không hủy được thì MỖI lần dừng
                // host chậm theo (đo khi thi công C3: bộ Integration 2 phút → 5,5 phút, StartupConfigurationTests 4 → 41 giây).
                var multiplexer = await redis.GetAsync().WaitAsync(stoppingToken);
                await multiplexer.GetSubscriber().SubscribeAsync(channel, (_, message) =>
                {
                    if (message.HasValue)
                        cache.Invalidate(message.ToString());
                }).WaitAsync(stoppingToken);
                multiplexer.ConnectionRestored += (_, _) => cache.InvalidateAll();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or TimeoutException)
            {
                // Log mức Warning, không kèm tin nhắn nào (chỉ là mã vai trò, nhưng giữ nếp không log payload).
                logger.LogWarning(ex, "Chưa đăng ký được kênh thay đổi quyền — thử lại sau {DelaySeconds} giây", delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }
}
