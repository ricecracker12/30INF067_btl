using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.Modules.Content.Infrastructure.Feed;

/// <summary>
/// Hiện thực <see cref="IFeedPageCache"/> trên Redis — khóa <c>feed:p1:{userId:D}</c>, TTL 30s, fail-open, cùng khuôn
/// <c>FeedSourceReader</c>/<c>FeedSourceCache</c> của SocialGraph (C1).
///
/// Giá trị là JSON đúng BỐN trường <c>{"ids":[…],"mode":"network","next":"…","fp":"…"}</c> (Đ-4.8, Đ-4.9) — không có
/// <c>PostResponse</c>, URL đã ký, <c>canEdit</c>. <see cref="Payload"/> là kiểu riêng của lớp này chứ không serialize thẳng
/// <see cref="CachedFeedPage"/>: thêm một thuộc tính vào record của Application không được lặng lẽ thêm một trường vào Redis.
/// </summary>
internal sealed class RedisFeedPageCache(
    RedisConnection redis,
    IOptions<FeedPageCacheOptions> options,
    FailOpenLogThrottle failOpenLog,
    ILogger<RedisFeedPageCache> logger) : IFeedPageCache
{
    internal const int TtlSeconds = 30;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary><c>Guid</c> in <c>:D</c> — một hàm cho cả đọc, ghi, xóa; hai chỗ gõ khóa khác format là xóa không bao giờ trúng.</summary>
    internal static string Key(Guid userId) => $"feed:p1:{userId:D}";

    public async Task<CachedFeedPage?> GetAsync(Guid userId, CancellationToken ct)
    {
        if (!options.Value.Enabled || Database() is not { } database)
            return null;

        try
        {
            var raw = await database.StringGetAsync(Key(userId));
            return raw.HasValue ? TryDeserialize(raw!) : null;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
            return null;
        }
    }

    public async Task SetAsync(Guid userId, CachedFeedPage page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!options.Value.Enabled || Database() is not { } database)
            return;

        var payload = new Payload(
            [.. page.Ids],
            page.Mode == FeedMode.Suggested ? SuggestedMode : NetworkMode,
            page.Next,
            page.Fingerprint);

        try
        {
            await database.StringSetAsync(
                Key(userId), JsonSerializer.Serialize(payload, Json), TimeSpan.FromSeconds(TtlSeconds));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
        }
    }

    /// <summary>
    /// Xóa cả khi công tắc TẮT: tắt rồi bật lại trong vòng 30s thì khóa cũ của tác giả vẫn còn đó — xóa thừa không tốn gì.
    /// </summary>
    public async Task InvalidateAsync(Guid authorId, CancellationToken ct)
    {
        if (Database() is not { } database)
            return;

        try
        {
            await database.KeyDeleteAsync(Key(authorId));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
        }
    }

    private const string NetworkMode = "network";
    private const string SuggestedMode = "suggested";

    /// <summary>Không chờ kết nối: Redis chưa nối xong hay đã rớt thì coi như không có cache (Đ-4.8, UC-08 E2).</summary>
    private IDatabase? Database()
    {
        if (redis.ConnectedOrNull() is { } connection)
            return connection.GetDatabase();

        LogFailOpen(null);
        return null;
    }

    /// <summary>Giá trị hỏng / lạ (khóa bị ghi tay, định dạng đổi giữa hai bản deploy) → coi như trượt, không ném.</summary>
    private static CachedFeedPage? TryDeserialize(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(json, Json);
            if (payload?.Ids is null || payload.Fp is null)
                return null;

            FeedMode? mode = payload.Mode switch
            {
                NetworkMode => FeedMode.Network,
                SuggestedMode => FeedMode.Suggested,
                _ => null,
            };

            return mode is { } m ? new CachedFeedPage(payload.Ids, m, payload.Next, payload.Fp) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void LogFailOpen(Exception? ex)
    {
        if (failOpenLog.ShouldLog("feed-page", out var suppressed))
            logger.LogWarning(
                ex,
                "Redis không sẵn sàng — bỏ qua cache trang đầu feed (fail-open, Đ-4.8); {Suppressed} lần cùng loại trước đó không ghi log",
                suppressed);
    }

    private sealed record Payload(
        [property: JsonPropertyName("ids")] Guid[] Ids,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("next")] string? Next,
        [property: JsonPropertyName("fp")] string Fp);
}
