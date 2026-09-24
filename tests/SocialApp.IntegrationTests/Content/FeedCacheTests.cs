using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// B4 — luật cache của feed trên Redis THẬT (Đ-4.8, Đ-4.9, Q-C1): <c>FEED-07b</c>, <c>FEED-09</c>, <c>FEED-09b</c>,
/// <c>FEED-10</c>, <c>FEED-13</c>.
///
/// Khuôn chung: đọc lần 1 → <b>khẳng định khóa <c>feed:p1:{id}</c> tồn tại</b> → gây thay đổi → đọc lần 2. Bỏ bước khẳng
/// định khóa thì một feed không cache gì vẫn qua cả năm ca — đúng loại lưới giả B1 dựng Redis thật để chặn.
/// <c>FakeObjectStorage.DistinctGetUrls</c> bật: mỗi lần ký một URL khác, để phân biệt "hydrate ký lại" với "cache trả URL
/// cũ" (L2).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedCacheTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Trước CreateClient đầu tiên: không gọi thì Redis là cổng 1, cache fail-open, và ca cache xanh vì lý do sai.
        factory.UseRedis(redis.ConnectionString);
        factory.Storage.DistinctGetUrls = true;
        await factory.UseFreshDatabaseAsync(postgres);

        // Kết nối Redis của APP mở ở nền lúc host khởi động — chưa xong thì lượt đọc đầu không ghi khóa.
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string PageKey(Guid userId) => $"feed:p1:{userId:D}";

    private static async Task<Guid> OnboardAsync(ModulesTestClient client)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = "Người dùng cache" });
        return id;
    }

    private async Task AssertCachedAsync(Guid userId) =>
        Assert.True(await redis.Database.KeyExistsAsync(PageKey(userId)),
            "đọc trang đầu xong mà không có feed:p1 — ca cache này sẽ xanh vì lý do sai");

    private static IReadOnlyList<Guid> Ids(FeedPage page) => [.. page.Items.Select(i => i.PostId)];

    /// <summary>
    /// Đ-4.9 (L5): giá trị thô của <c>feed:p1:{userId}</c> có ĐÚNG bốn trường <c>ids</c>, <c>mode</c>, <c>next</c>, <c>fp</c> —
    /// không <c>canEdit</c>, <c>media</c>, <c>author</c>, không URL. Tách thành helper để GĐ3 gọi lại khi thêm
    /// <c>myReaction</c>: trường mới của hydrate KHÔNG được lọt vào cache.
    /// </summary>
    internal static void AssertRawValueHoldsOnlyIds(IDatabase database, Guid userId)
    {
        var raw = (string?)database.StringGet(PageKey(userId));
        Assert.NotNull(raw);
        Assert.DoesNotContain("fake.invalid", raw, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(raw);
        Assert.Equal(["ids", "mode", "next", "fp"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.All(json.RootElement.GetProperty("ids").EnumerateArray(), id => id.GetGuid());
    }

    /// <summary>
    /// <c>FEED-07b</c> (L15, Q-C1) — lát cắt F2: A mới tinh đọc feed (gợi ý, được cache) → kết bạn với B (B có bài) → đọc
    /// lại NGAY, khi khóa vẫn còn: <c>mode = network</c>, có bài của B. Bỏ so dấu nguồn thì A thấy trang gợi ý cũ tới 30s.
    /// </summary>
    [Fact]
    public async Task FEED_07b_vua_ket_noi_dau_tien_thi_khong_con_trang_goi_y_cu()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        var stranger = await OnboardAsync(client);
        await client.CreatePostOkAsync(stranger, new { body = "Công khai của người lạ.", privacy = "public" });
        var ofB = await client.CreatePostOkAsync(b, new { body = "Bài của B.", privacy = "friends" });

        var before = await client.GetFeedOkAsync(a);
        Assert.Equal(FeedMode.Suggested, before.Mode);
        await AssertCachedAsync(a);

        await client.MakeFriendsAsync(a, b);
        await AssertCachedAsync(a);   // khóa vẫn còn — TTL 30s chưa hết, không ai xóa feed:p1 của A

        var after = await client.GetFeedOkAsync(a);
        Assert.Equal(FeedMode.Network, after.Mode);
        Assert.Contains(ofB.PostId, Ids(after));
    }

    /// <summary>
    /// <c>FEED-09</c> — A và B là bạn VÀ A theo dõi B. A đọc (khóa có) → A hủy kết bạn → A đọc lại và cuộn hết: không còn bài
    /// <c>friends</c> của B, bài <c>public</c> còn đủ, không lặp bài nào qua các trang. "A theo dõi B" là cần: thiếu nó B biến
    /// mất hẳn khỏi nguồn, và ca không phân biệt được "loại đúng bài" với "loại cả người" (Đ-4.5).
    /// </summary>
    [Fact]
    public async Task FEED_09_huy_ket_ban_khi_trang_dau_dang_cache_thi_mat_bai_friends_con_public()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        await client.FollowOkAsync(a, b);

        var friendsPosts = new List<Guid>();
        var publicPosts = new List<Guid>();
        friendsPosts.Add((await client.CreatePostOkAsync(b, new { body = "friends đầu.", privacy = "friends" })).PostId);
        for (var i = 0; i < 21; i++)
            publicPosts.Add((await client.CreatePostOkAsync(b, new { body = $"public {i}.", privacy = "public" })).PostId);
        friendsPosts.Add((await client.CreatePostOkAsync(b, new { body = "friends cuối.", privacy = "friends" })).PostId);

        var cachedPage = await client.GetFeedOkAsync(a);
        Assert.Contains(friendsPosts[1], Ids(cachedPage));
        await AssertCachedAsync(a);

        await client.UnfriendOkAsync(a, b);

        var seen = new List<Guid>();
        var page = await client.GetFeedOkAsync(a);
        seen.AddRange(Ids(page));
        while (page.NextCursor is { } next)
        {
            page = await client.GetFeedOkAsync(a, $"?cursor={Uri.EscapeDataString(next)}");
            seen.AddRange(Ids(page));
        }

        Assert.Empty(seen.Intersect(friendsPosts));
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(publicPosts.OrderBy(id => id), seen.OrderBy(id => id));
    }

    /// <summary>
    /// <c>FEED-09b</c> (L11) — A CHỈ theo dõi B; B có bài <c>public</c>. A đọc (khóa có) → B đổi bài đó sang <c>friends</c> →
    /// A đọc lại, cache VẪN trúng (dấu nguồn không đổi — giá trị khóa y nguyên, vẫn chứa id bài đó): bài không còn. Lưới duy
    /// nhất của bước kiểm lại BR-02 ở <c>FeedService</c>.
    /// </summary>
    [Fact]
    public async Task FEED_09b_bai_doi_sang_friends_khi_cache_van_trung_thi_bi_loai()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.FollowOkAsync(a, b);
        var post = await client.CreatePostOkAsync(b, new { body = "Lúc đầu công khai.", privacy = "public" });

        Assert.Contains(post.PostId, Ids(await client.GetFeedOkAsync(a)));
        await AssertCachedAsync(a);
        var rawBefore = (string?)await redis.Database.StringGetAsync(PageKey(a));

        await client.UpdatePostOkAsync(b, post.PostId, new { privacy = "friends" });
        var after = await client.GetFeedOkAsync(a);

        // Trúng cache: trượt thì FeedService ghi lại khóa với ids mới (không còn bài này, vì SQL đã lọc).
        Assert.Equal(rawBefore, (string?)await redis.Database.StringGetAsync(PageKey(a)));
        Assert.Contains(post.PostId.ToString("D"), rawBefore!, StringComparison.Ordinal);
        Assert.DoesNotContain(post.PostId, Ids(after));
    }

    /// <summary>
    /// <c>FEED-10</c> (L2) — bài có ảnh. Lần 2 trúng cache mà URL ảnh KHÁC lần 1 (hydrate ký lại mỗi lần), và giá trị thô
    /// trong Redis không chứa URL. Cache lưu <c>PostResponse</c> thì URL lần 2 bằng lần 1 — ảnh vỡ sau 15 phút trên thật.
    /// </summary>
    [Fact]
    public async Task FEED_10_trung_cache_van_ky_URL_anh_moi_va_Redis_khong_giu_URL()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var post = await client.CreatePostOkAsync(
            b, new { body = "Có ảnh.", privacy = "friends", mediaKeys = new[] { client.PutPostObject(b) } });

        var first = await client.GetFeedOkAsync(a);
        await AssertCachedAsync(a);
        var second = await client.GetFeedOkAsync(a);

        var url1 = first.Items.Single(i => i.PostId == post.PostId).Media.Single().Url;
        var url2 = second.Items.Single(i => i.PostId == post.PostId).Media.Single().Url;
        Assert.NotEqual(url1, url2);
        Assert.DoesNotContain("fake.invalid", (string?)await redis.Database.StringGetAsync(PageKey(a)) ?? "",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>FEED-13</c> (L5) — A và B là bạn, mỗi người một bài; mỗi người đọc feed CỦA MÌNH hai lần (lần 2 trúng):
    /// <c>canEdit = true</c> CHỈ trên bài của người đọc. Giá trị thô của CẢ HAI khóa đúng bốn trường. Khóa theo người xem nên
    /// cache lưu <c>PostResponse</c> vẫn cho <c>canEdit</c> đúng — vế giá trị thô mới là thứ bắt được đột biến đó.
    ///
    /// GĐ3 (Đ-3.11, rủi ro GĐ4-01): mở rộng với <c>myReaction</c> — A thả love vào bài của B; A thấy love, B thấy null trên CÙNG bài;
    /// A đổi sang haha rồi đọc lần nữa (vẫn trúng cache) thấy ngay haha. <c>myReaction</c> hydrate từ DB mỗi lần, không nằm trong
    /// cache dùng chung.
    /// </summary>
    [Fact]
    public async Task FEED_13_canEdit_dung_nguoi_xem_va_gia_tri_tho_chi_co_id()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var ofA = await client.CreatePostOkAsync(a, new { body = "Bài của A.", privacy = "friends" });
        var ofB = await client.CreatePostOkAsync(b, new { body = "Bài của B.", privacy = "friends" });
        await client.ReactOkAsync(a, "posts", ofB.PostId, "love");

        foreach (var (reader, own, other) in new[] { (a, ofA.PostId, ofB.PostId), (b, ofB.PostId, ofA.PostId) })
        {
            await client.GetFeedOkAsync(reader);
            await AssertCachedAsync(reader);
            var hit = await client.GetFeedOkAsync(reader);

            Assert.True(hit.Items.Single(i => i.PostId == own).CanEdit);
            Assert.False(hit.Items.Single(i => i.PostId == other).CanEdit);
            Assert.Equal(reader == a ? ReactionType.Love : (ReactionType?)null, hit.Items.Single(i => i.PostId == ofB.PostId).MyReaction);
            Assert.Null(hit.Items.Single(i => i.PostId == ofA.PostId).MyReaction);
            AssertRawValueHoldsOnlyIds(redis.Database, reader);
        }

        await client.ReactOkAsync(a, "posts", ofB.PostId, "haha");
        await AssertCachedAsync(a);
        var afterChange = await client.GetFeedOkAsync(a);
        Assert.Equal(ReactionType.Haha, afterChange.Items.Single(i => i.PostId == ofB.PostId).MyReaction);
        AssertRawValueHoldsOnlyIds(redis.Database, a);
    }
}
