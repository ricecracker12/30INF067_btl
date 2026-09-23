using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Kênh pub/sub "tập quyền của một vai trò vừa đổi" (Đ-6.10). Có tiền tố môi trường — cùng lý do <c>ChannelPrefix</c> của backplane
/// GĐ5 (Đ-5.13): staging và production chung một Redis thì không được nghe lẫn nhau. Tin nhắn là mã vai trò trần.
/// </summary>
public static class PermissionsChangedChannel
{
    public static string For(string environmentName) =>
        $"socialapp:{environmentName.ToLowerInvariant()}:authz:permissions-changed";

    /// <summary>Tên môi trường của host; container trần (unit test) không có host → <c>"default"</c>.</summary>
    internal static string For(IServiceProvider services) =>
        For(services.GetService<IHostEnvironment>()?.EnvironmentName ?? "default");
}

/// <summary>
/// Hiện thực <see cref="IPermissionChangeNotifier"/>: <see cref="IPermissionCache.Invalidate"/> TRƯỚC, rồi <c>PUBLISH</c> trên kết nối
/// Redis chung. Không có <see cref="RedisConnection"/> (container trần) → chỉ xóa tại chỗ.
/// </summary>
internal sealed class PermissionChangeNotifier(
    IPermissionCache cache,
    IServiceProvider services,
    ILogger<PermissionChangeNotifier> logger) : IPermissionChangeNotifier
{
    private readonly string _channel = PermissionsChangedChannel.For(services);

    // Tra lúc chạy, không tiêm: DI không coi tham số nullable là tùy chọn, và container trần (unit test) không có Redis.
    private readonly FailOpenLogThrottle? _failOpenLog = services.GetService<FailOpenLogThrottle>();

    public async Task NotifyAsync(string roleCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(roleCode);

        // Tại chỗ TRƯỚC: request kế tiếp trên CHÍNH instance này không phụ thuộc Redis (PERM-01).
        cache.Invalidate(roleCode);

        if (services.GetService<RedisConnection>()?.ConnectedOrNull() is not { } redis)
        {
            LogPublishFailed(null);
            return;
        }

        try
        {
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(_channel), roleCode);
        }
        // RedisTimeoutException KHÔNG kế thừa RedisException — bắt riêng (cùng bài học RedisTokenRevocationStore).
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogPublishFailed(ex);
        }
    }

    private void LogPublishFailed(Exception? ex)
    {
        long suppressed = 0;
        if (_failOpenLog is not null && !_failOpenLog.ShouldLog("permissions-publish", out suppressed))
            return;

        logger.LogWarning(ex,
            "Không phát được thay đổi quyền qua Redis — instance khác thấy sau tối đa {TtlSeconds} giây (Đ-6.10); {Suppressed} lần cùng loại trước đó không ghi log",
            (int)PermissionCache.Ttl.TotalSeconds, suppressed);
    }
}
