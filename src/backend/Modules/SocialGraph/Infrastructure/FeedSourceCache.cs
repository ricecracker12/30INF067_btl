using Microsoft.Extensions.Logging;
using SocialApp.Modules.SocialGraph.Application;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.Modules.SocialGraph.Infrastructure;

/// <summary>
/// Xóa khóa <c>sg:feed-sources:*</c> sau <c>COMMIT</c> (Đ-4.15). Cùng hàm <see cref="Key"/> với
/// <see cref="FeedSourceReader"/> — hai chỗ gõ chuỗi khóa khác format là xóa không bao giờ trúng.
/// </summary>
internal sealed class FeedSourceCache(RedisConnection redis, ILogger<FeedSourceCache> logger) : IFeedSourceCache
{
    internal const int TtlSeconds = 60;

    /// <summary><c>Guid</c> in <c>:D</c> (chữ thường, có gạch). <c>ToString("N")</c> là khóa khác.</summary>
    internal static string Key(Guid userId) => $"sg:feed-sources:{userId:D}";

    public async Task InvalidateAsync(Guid userId, Guid? otherUserId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (redis.ConnectedOrNull() is not { } connection)
        {
            LogFailOpen(null);
            return;
        }

        RedisKey[] keys = otherUserId is { } other
            ? [Key(userId), Key(other)]
            : [Key(userId)];

        try
        {
            await connection.GetDatabase().KeyDeleteAsync(keys);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
        }
    }

    private void LogFailOpen(Exception? ex) =>
        logger.LogWarning(ex, "Redis không sẵn sàng — bỏ qua xóa cache nguồn feed (fail-open, Đ-4.8)");
}
