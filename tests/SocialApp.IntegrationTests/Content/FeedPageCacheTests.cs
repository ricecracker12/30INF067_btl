using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.SharedKernel.Redis;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// C4 — cache trang đầu <c>feed:p1:{id}</c> trên Redis thật (Đ-4.8, Đ-4.9): round-trip, giá trị thô đúng BỐN trường, TTL
/// 30s, Redis chết → <c>null</c> không ném, công tắc tắt → không đọc không ghi; và <c>PostService</c> xóa khóa của TÁC GIẢ
/// sau khi đăng / sửa / xóa bài — qua HTTP thật.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedPageCacheTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    /// <summary>AddContentModule cần chuỗi kết nối để đăng ký DbContext; test cache trần không mở kết nối nào.</summary>
    private const string UnusedPostgres = "Host=127.0.0.1;Port=1;Database=unused";

    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Key(Guid userId) => $"feed:p1:{userId:D}";

    private static async Task<ServiceProvider> BareAsync(string redisConnection, bool enabled = true)
    {
        var services = new ServiceCollection()
            .AddContentModule(UnusedPostgres)
            .AddSharedKernelRedis(redisConnection)
            .AddLogging()
            .Configure<FeedPageCacheOptions>(o => o.Enabled = enabled)
            .BuildServiceProvider();

        if (redisConnection != ApiFactory.UnreachableRedis)
            // ServiceCollection trần không chạy RedisConnectionStarter — GetAsync mới bắt đầu kết nối.
            Assert.True((await services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
        return services;
    }

    [Fact]
    public async Task Ghi_roi_doc_lai_dung_gia_tri_TTL_30s()
    {
        await using var services = await BareAsync(redis.ConnectionString);
        var cache = services.GetRequiredService<IFeedPageCache>();
        var me = Guid.NewGuid();
        var page = new CachedFeedPage([Guid.NewGuid(), Guid.NewGuid()], FeedMode.Suggested, "abc", "dau-nguon");

        await cache.SetAsync(me, page, CancellationToken.None);
        var read = await cache.GetAsync(me, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(page.Ids, read.Ids);
        Assert.Equal(FeedMode.Suggested, read.Mode);
        Assert.Equal("abc", read.Next);
        Assert.Equal("dau-nguon", read.Fingerprint);

        var ttl = await redis.Database.KeyTimeToLiveAsync(Key(me));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 20, 30);
    }

    /// <summary>
    /// Đ-4.9: giá trị thô chỉ có <c>ids</c>, <c>mode</c>, <c>next</c>, <c>fp</c> — <c>mode</c> là chuỗi chữ thường như trên dây,
    /// <c>next</c> null khi hết dữ liệu.
    /// </summary>
    [Fact]
    public async Task Gia_tri_tho_dung_bon_truong()
    {
        await using var services = await BareAsync(redis.ConnectionString);
        var me = Guid.NewGuid();
        var id = Guid.NewGuid();

        await services.GetRequiredService<IFeedPageCache>()
            .SetAsync(me, new CachedFeedPage([id], FeedMode.Network, null, "fp"), CancellationToken.None);

        using var json = JsonDocument.Parse((string)(await redis.Database.StringGetAsync(Key(me)))!);
        Assert.Equal(["ids", "mode", "next", "fp"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(id, json.RootElement.GetProperty("ids")[0].GetGuid());
        Assert.Equal("network", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("next").ValueKind);
    }

    [Fact]
    public async Task Redis_chet_doc_ra_null_ghi_va_xoa_khong_nem()
    {
        await using var services = await BareAsync(ApiFactory.UnreachableRedis);
        var cache = services.GetRequiredService<IFeedPageCache>();
        var me = Guid.NewGuid();

        await cache.SetAsync(me, new CachedFeedPage([], FeedMode.Network, null, "fp"), CancellationToken.None);
        await cache.InvalidateAsync(me, CancellationToken.None);

        Assert.Null(await cache.GetAsync(me, CancellationToken.None));
    }

    [Fact]
    public async Task Cong_tac_tat_thi_khong_ghi_va_khong_doc()
    {
        await using var services = await BareAsync(redis.ConnectionString, enabled: false);
        var cache = services.GetRequiredService<IFeedPageCache>();
        var me = Guid.NewGuid();

        await cache.SetAsync(me, new CachedFeedPage([], FeedMode.Network, null, "fp"), CancellationToken.None);
        Assert.False(await redis.Database.KeyExistsAsync(Key(me)));

        // Có khóa sẵn (bật rồi tắt trong vòng 30s) — tắt thì vẫn không đọc.
        await redis.Database.StringSetAsync(Key(me), """{"ids":[],"mode":"network","next":null,"fp":"fp"}""");
        Assert.Null(await cache.GetAsync(me, CancellationToken.None));
    }

    /// <summary>Giá trị hỏng (ghi tay, định dạng đổi giữa hai bản deploy) → coi như trượt, không 500.</summary>
    [Fact]
    public async Task Gia_tri_hong_thi_coi_nhu_truot()
    {
        await using var services = await BareAsync(redis.ConnectionString);
        var me = Guid.NewGuid();
        await redis.Database.StringSetAsync(Key(me), "{khong phai json");

        Assert.Null(await services.GetRequiredService<IFeedPageCache>().GetAsync(me, CancellationToken.None));
    }

    /// <summary>
    /// Đ-4.8: tác giả đăng / sửa / xóa bài → khóa trang đầu của CHÍNH tác giả mất; khóa của người khác (bạn bè thấy bài mới
    /// trễ ≤ 30s là cái giá đã chấp nhận) còn nguyên.
    /// </summary>
    [Fact]
    public async Task Dang_sua_xoa_bai_xoa_khoa_cua_tac_gia_khong_dung_khoa_nguoi_khac()
    {
        var client = new ModulesTestClient(factory);
        // Kết nối Redis của APP mở ở nền lúc host khởi động; chưa xong thì InvalidateAsync fail-open và ca này đỏ vì lý do sai.
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
        var author = Guid.NewGuid();
        var other = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Tác giả C4" });

        await SeedKeysAsync(author, other);
        var post = await client.CreatePostOkAsync(author, new { body = "Bài mới.", privacy = "public" });
        Assert.False(await redis.Database.KeyExistsAsync(Key(author)), "đăng bài phải xóa feed:p1 của tác giả");
        Assert.True(await redis.Database.KeyExistsAsync(Key(other)));

        await SeedKeysAsync(author, other);
        await client.UpdatePostOkAsync(author, post.PostId, new { body = "Đã sửa." });
        Assert.False(await redis.Database.KeyExistsAsync(Key(author)), "sửa bài phải xóa feed:p1 của tác giả");
        Assert.True(await redis.Database.KeyExistsAsync(Key(other)));

        await SeedKeysAsync(author, other);
        using var deleted = await client.DeletePostAsync(author, post.PostId);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(await redis.Database.KeyExistsAsync(Key(author)), "xóa bài phải xóa feed:p1 của tác giả");
        Assert.True(await redis.Database.KeyExistsAsync(Key(other)));
    }

    private async Task SeedKeysAsync(params Guid[] users)
    {
        foreach (var user in users)
            await redis.Database.StringSetAsync(
                Key(user), """{"ids":[],"mode":"network","next":null,"fp":"fp"}""", TimeSpan.FromSeconds(30));
    }
}
