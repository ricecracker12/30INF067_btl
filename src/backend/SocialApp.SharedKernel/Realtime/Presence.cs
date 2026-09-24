using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// "Người này có đang kết nối hub nào không" (Đ-5.11). Người tiêu thụ là GĐ6 (Đ-6.17): thông báo <c>message</c> chỉ tạo khi người
/// nhận offline. GĐ5 KHÔNG dùng presence để quyết định có đẩy tin hay không — <c>Clients.User</c> đẩy tới ai đang kết nối.
/// </summary>
public interface IPresenceReader
{
    /// <summary>
    /// <c>true</c> khi người dùng có ít nhất một kết nối hub còn hạn. Redis không trả lời được → <c>false</c>: với người tiêu thụ
    /// duy nhất (thông báo), coi là offline là phía an toàn — thừa một thông báo hơn là mất một thông báo.
    /// </summary>
    Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Presence trong Redis (Đ-5.11): <c>rt:presence:{userId}</c> là sorted set, member = <c>connectionId</c>, score = thời điểm hết
/// hạn (unix ms, now + 90s). Online ⇔ có member với score &gt; now.
///
/// KHÔNG tin vào <c>OnDisconnectedAsync</c>: nó không chạy khi process chết (deploy, OOM, <c>docker kill</c>). Mỗi instance tự gia
/// hạn MỖI 30 giây các kết nối NÓ đang giữ (<see cref="ExecuteAsync"/>); kết nối của instance đã chết không ai gia hạn nên tự
/// hết hạn sau ≤ 90 giây — không "online vĩnh viễn" sau mỗi lần deploy (R5-06).
/// </summary>
public sealed class RedisPresenceTracker(
    RedisConnection redis,
    TimeProvider clock,
    ILogger<RedisPresenceTracker> logger) : BackgroundService, IPresenceReader
{
    public const string KeyPrefix = "rt:presence:";

    /// <summary>Hạn của một lần ghi — gấp ba nhịp gia hạn, chịu được hai lượt gia hạn trượt.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    /// <summary>Nhịp gia hạn các kết nối của instance này.</summary>
    public static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, string> _local = new();   // connectionId → userId, chỉ của instance này

    public static string Key(string userId) => KeyPrefix + userId;

    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
    {
        if (redis.ConnectedOrNull() is not { } connection)
            return false;

        try
        {
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var alive = await connection.GetDatabase().SortedSetLengthAsync(Key(userId.ToString()), now, double.PositiveInfinity, Exclude.Start);
            return alive > 0;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis không sẵn sàng — không đọc được presence (coi là offline)");
            return false;
        }
    }

    /// <summary>Kết nối mới của <paramref name="userId"/> trên instance này (gọi từ <see cref="PresenceHubFilter"/>).</summary>
    public Task ConnectedAsync(string connectionId, string userId)
    {
        _local[connectionId] = userId;
        return WriteAsync(connectionId, userId);
    }

    /// <summary>Kết nối đóng bình thường — xóa ngay, không đợi hết hạn.</summary>
    public async Task DisconnectedAsync(string connectionId)
    {
        if (!_local.TryRemove(connectionId, out var userId) || redis.ConnectedOrNull() is not { } connection)
            return;

        try
        {
            await connection.GetDatabase().SortedSetRemoveAsync(Key(userId), connectionId);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis không sẵn sàng — không xóa được presence (tự hết hạn sau {Ttl})", Ttl);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RenewEvery, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var (connectionId, userId) in _local)
                await WriteAsync(connectionId, userId);
        }
    }

    private async Task WriteAsync(string connectionId, string userId)
    {
        if (redis.ConnectedOrNull() is not { } connection)
            return;

        try
        {
            var db = connection.GetDatabase();
            var key = Key(userId);
            var now = clock.GetUtcNow();
            await db.SortedSetAddAsync(key, connectionId, now.Add(Ttl).ToUnixTimeMilliseconds());
            // Dọn member đã hết hạn (của instance đã chết) và cho cả khóa một TTL — người không bao giờ quay lại không để khóa mồ côi.
            await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now.ToUnixTimeMilliseconds());
            await db.KeyExpireAsync(key, Ttl);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis không sẵn sàng — không ghi được presence");
        }
    }
}

/// <summary>Filter TOÀN CỤC ghi presence cho MỌI hub (Đ-5.11): kết nối → online, ngắt → xóa. Không kiểm quyền gì.</summary>
public sealed class PresenceHubFilter(RedisPresenceTracker tracker) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (context.Context.UserIdentifier is { } userId)
            await tracker.ConnectedAsync(context.Context.ConnectionId, userId);
        await next(context);
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        await tracker.DisconnectedAsync(context.Context.ConnectionId);
        await next(context, exception);
    }
}
