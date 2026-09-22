using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.Modules.SocialGraph.Application;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.Modules.SocialGraph.Infrastructure;

/// <summary>
/// Hiện thực <see cref="IFeedSourceReader"/> kèm cache Redis <c>sg:feed-sources:{userId}</c> TTL 60s, fail-open (Đ-4.8).
/// </summary>
internal sealed class FeedSourceReader(
    SocialGraphDbContext db,
    RedisConnection redis,
    IOptions<FeedSourceCacheOptions> options,
    FailOpenLogThrottle failOpenLog,
    ILogger<FeedSourceReader> logger) : IFeedSourceReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FeedSources> GetAsync(Guid userId, CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return await ReadFromDbAsync(userId, ct);

        if (redis.ConnectedOrNull() is not { } connection)
        {
            LogFailOpen(null);
            return await ReadFromDbAsync(userId, ct);
        }

        var cache = connection.GetDatabase();
        var key = FeedSourceCache.Key(userId);

        try
        {
            var cached = await cache.StringGetAsync(key);
            if (cached.HasValue && TryDeserialize(cached!, out var hit))
                return hit;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
            return await ReadFromDbAsync(userId, ct);
        }

        var fresh = await ReadFromDbAsync(userId, ct);

        try
        {
            await cache.StringSetAsync(
                key,
                Serialize(fresh),
                TimeSpan.FromSeconds(FeedSourceCache.TtlSeconds));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
        }

        return fresh;
    }

    private async Task<FeedSources> ReadFromDbAsync(Guid userId, CancellationToken ct)
    {
        // Bạn bè: tôi ở một trong hai đầu cặp. Nửa "tôi là min" đi PK, nửa "tôi là max" đi idx_friendships_user_max.
        var friends = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted
                        && (f.UserMinId == userId || f.UserMaxId == userId))
            .Select(f => f.UserMinId == userId ? f.UserMaxId : f.UserMinId)
            .ToListAsync(ct);

        var following = await db.Follows.AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .Select(f => f.FolloweeId)
            .ToListAsync(ct);

        var friendSet = friends.ToHashSet();
        return new FeedSources(friendSet, following.Where(id => !friendSet.Contains(id)).ToHashSet());
    }

    private static string Serialize(FeedSources sources) =>
        JsonSerializer.Serialize(new CachedPayload(sources.Friends.ToArray(), sources.FollowingOnly.ToArray()), Json);

    private static bool TryDeserialize(string json, out FeedSources sources)
    {
        sources = new FeedSources(new HashSet<Guid>(), new HashSet<Guid>());
        try
        {
            var payload = JsonSerializer.Deserialize<CachedPayload>(json, Json);
            if (payload?.F is null || payload.Fo is null)
                return false;

            sources = new FeedSources(payload.F.ToHashSet(), payload.Fo.ToHashSet());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void LogFailOpen(Exception? ex)
    {
        if (failOpenLog.ShouldLog("feed-sources-read", out var suppressed))
            logger.LogWarning(
                ex,
                "Redis không sẵn sàng — bỏ qua cache nguồn feed (fail-open, Đ-4.8); {Suppressed} lần cùng loại trước đó không ghi log",
                suppressed);
    }

    /// <summary>Ghi mảng, đọc mảng, dựng set — <c>HashSet&lt;Guid&gt;</c> không round-trip như mong đợi.</summary>
    private sealed record CachedPayload(
        [property: JsonPropertyName("f")] Guid[] F,
        [property: JsonPropertyName("fo")] Guid[] Fo);
}
