using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Serilog.Core;
using Serilog.Events;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Application.Posts;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// B4 — nghiệm thu feed qua HTTP trên Postgres thật (Mục 10.2): ma trận quyền Đ-4.5, feed gợi ý Đ-4.6, degrade khi Redis
/// chết, 503 khi DB quá tải, cursor rác.
///
/// Harness mặc định — Redis cổng 1, KHÔNG cache — có chủ đích: các ca này canh TRUY VẤN, và có cache thì bug truy vấn bị che
/// bởi dữ liệu cũ. <c>FEED-11</c> nằm ở đây vì harness mặc định CHÍNH LÀ "Redis dừng". Ca cache ở <c>FeedCacheTests</c>.
///
/// Kỳ vọng viết tay theo bảng Mục 10.2 (luật 8) — không đọc <c>FeedVisibility</c> hay hằng của code.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> OnboardAsync(ModulesTestClient client, string name = "Người dùng B4")
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = name });
        return id;
    }

    private static Task<PostResponse> PostAsync(ModulesTestClient client, Guid author, string body, string privacy) =>
        client.CreatePostOkAsync(author, new { body, privacy });

    private static IReadOnlyList<Guid> Ids(FeedPage page) => [.. page.Items.Select(i => i.PostId)];

    /// <summary>
    /// <c>FEED-01</c> (US-008 AC-01) — 25 bài của MỘT bạn (một tác giả, dưới rate limit), <c>limit=20</c>: trang 1 đủ 20, mới
    /// trước, có <c>nextCursor</c>; trang 2 đủ 5, <c>nextCursor = null</c>; không trùng bài giữa hai trang.
    /// </summary>
    [Fact]
    public async Task FEED_01_hai_trang_du_25_bai_cua_ban_khong_trung()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        for (var i = 0; i < 25; i++)
            await PostAsync(client, b, $"Bài số {i}.", "friends");

        var page1 = await client.GetFeedOkAsync(a, "?limit=20");
        Assert.Equal(20, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal("Bài số 24.", page1.Items[0].Body);

        var page2 = await client.GetFeedOkAsync(a, $"?limit=20&cursor={Uri.EscapeDataString(page1.NextCursor)}");
        Assert.Equal(5, page2.Items.Count);
        Assert.Null(page2.NextCursor);
        Assert.Equal("Bài số 0.", page2.Items[^1].Body);

        Assert.Equal(25, Ids(page1).Concat(Ids(page2)).Distinct().Count());
    }

    /// <summary><c>FEED-02</c> (AC-02) — bài <c>friends</c> của người lạ không xuất hiện.</summary>
    [Fact]
    public async Task FEED_02_bai_friends_cua_nguoi_la_khong_xuat_hien()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        var stranger = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var hidden = await PostAsync(client, stranger, "Chỉ bạn của người lạ.", "friends");

        var page = await client.GetFeedOkAsync(a);

        Assert.DoesNotContain(hidden.PostId, Ids(page));
    }

    /// <summary>
    /// <c>FEED-03</c> — chỉ theo dõi, không bạn: bài <c>friends</c> KHÔNG, bài <c>public</c> CÓ, <c>mode = network</c>. Vế
    /// <c>mode</c> là cần: người chỉ theo dõi mà rơi vào gợi ý thì bài <c>public</c> của người được theo dõi vẫn hiện, và ca
    /// này xanh nếu không soi <c>mode</c>.
    /// </summary>
    [Fact]
    public async Task FEED_03_chi_theo_doi_thay_public_khong_thay_friends_mode_network()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var c = await OnboardAsync(client);
        await client.FollowOkAsync(a, c);
        var friendsOnly = await PostAsync(client, c, "Bài cho bạn bè.", "friends");
        var open = await PostAsync(client, c, "Bài công khai.", "public");

        var page = await client.GetFeedOkAsync(a);

        Assert.Equal(FeedMode.Network, page.Mode);
        Assert.Contains(open.PostId, Ids(page));
        Assert.DoesNotContain(friendsOnly.PostId, Ids(page));
    }

    /// <summary><c>FEED-04</c> — bài <c>friends</c> của bạn xuất hiện. Cũng là lưới của DI-01 (khôi phục <c>AlwaysStrangers</c>).</summary>
    [Fact]
    public async Task FEED_04_bai_friends_cua_ban_xuat_hien()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var post = await PostAsync(client, b, "Bài cho bạn bè.", "friends");

        var page = await client.GetFeedOkAsync(a);

        Assert.Contains(post.PostId, Ids(page));
    }

    /// <summary><c>FEED-05</c> — một ca hai vế (Đ-4.5): <c>private</c> của bạn KHÔNG, <c>private</c> của mình CÓ.</summary>
    [Fact]
    public async Task FEED_05_private_cua_ban_khong_private_cua_minh_co()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var theirs = await PostAsync(client, b, "Riêng tư của bạn.", "private");
        var mine = await PostAsync(client, a, "Riêng tư của mình.", "private");

        var page = await client.GetFeedOkAsync(a);

        Assert.DoesNotContain(theirs.PostId, Ids(page));
        Assert.Contains(mine.PostId, Ids(page));
    }

    /// <summary><c>FEED-06</c> (AC-03, BR-07) — bài <c>hidden</c> của bạn không xuất hiện.</summary>
    [Fact]
    public async Task FEED_06_bai_hidden_cua_ban_khong_xuat_hien()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var post = await PostAsync(client, b, "Bài sẽ bị ẩn.", "public");

        // Luật 9 có ngoại lệ ở đây: GĐ6 mới có endpoint kiểm duyệt, nên đặt `hidden` thẳng bằng SQL.
        Assert.Equal(1, await client.ExecuteSqlAsync(
            "update content.posts set status = 'hidden', hidden_reason = 'B4' where post_id = $1", post.PostId));

        var page = await client.GetFeedOkAsync(a);

        Assert.DoesNotContain(post.PostId, Ids(page));
    }

    /// <summary>
    /// <c>FEED-07</c> (Đ-4.6) — chưa kết nối: <c>mode = suggested</c>, có bài <c>public</c> của người khác, KHÔNG có bài của mình.
    /// </summary>
    [Fact]
    public async Task FEED_07_chua_ket_noi_thi_goi_y_khong_co_bai_cua_minh()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var stranger = await OnboardAsync(client);
        await PostAsync(client, a, "Bài của chính mình.", "public");
        var theirs = await PostAsync(client, stranger, "Bài công khai của người lạ.", "public");

        var page = await client.GetFeedOkAsync(a);

        Assert.Equal(FeedMode.Suggested, page.Mode);
        Assert.Contains(theirs.PostId, Ids(page));
        Assert.DoesNotContain(page.Items, i => i.Author.UserId == a);
    }

    /// <summary>
    /// <c>FEED-08</c> — có một bạn chưa đăng gì, VÀ có bài <c>public</c> của người lạ: <c>mode = network</c>, rỗng,
    /// <c>nextCursor = null</c>. Không có bài người lạ thì ca này xanh cả khi code trộn gợi ý.
    /// </summary>
    [Fact]
    public async Task FEED_08_co_ban_chua_dang_gi_thi_rong_khong_tron_goi_y()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        var stranger = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        await PostAsync(client, stranger, "Công khai nhưng là người lạ.", "public");

        var page = await client.GetFeedOkAsync(a);

        Assert.Equal(FeedMode.Network, page.Mode);
        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    /// <summary>
    /// <c>FEED-11</c> (UC-08 E2) — Redis không tới được (harness mặc định): 200, nội dung đúng, và có log cảnh báo fail-open
    /// của cả hai tầng cache. Không 500, không màn trắng.
    /// </summary>
    [Fact]
    public async Task FEED_11_Redis_dung_van_200_dung_noi_dung_va_log_canh_bao()
    {
        var logs = new CapturingLogSink();
        await using var app = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(logs)));
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);
        var post = await PostAsync(client, b, "Bài lúc Redis chết.", "friends");

        using var http = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/feed");
        request.Headers.Authorization = ModulesTestClient.Bearer(a);
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<FeedPage>(ModulesTestClient.Json))!;
        Assert.Equal([post.PostId], Ids(page));

        var warnings = logs.Events
            .Where(e => e.Level == LogEventLevel.Warning)
            .Select(e => e.RenderMessage())
            .ToList();
        Assert.Contains(warnings, m => m.Contains("cache nguồn feed", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.Contains("cache trang đầu feed", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>FEED-12</c> (Đ-4.10, UC-08 E3; L3, L9, Q-B4) — truy vấn feed chờ khóa bảng quá 5s → 503, <c>Retry-After: 5</c>,
    /// <c>title</c> theo yaml, có <c>traceId</c>. KHÔNG khẳng định <c>feed.unavailable</c>: mã nội bộ, không lên dây.
    ///
    /// Người đọc có một bạn → nhánh LATERAL (không phải <c>idx_posts_public_recent</c>). Ca chờ đủ 5 giây thật (Q-B4). Khóa
    /// bằng một kết nối RIÊNG trong transaction, luôn rollback — không pg_sleep, không hook trong code sản phẩm (L3).
    /// </summary>
    [Fact]
    public async Task FEED_12_truy_van_qua_5s_tra_503_retry_after_5()
    {
        var client = new ModulesTestClient(factory);
        var a = await OnboardAsync(client);
        var b = await OnboardAsync(client);
        await client.MakeFriendsAsync(a, b);

        await using var locker = new NpgsqlConnection(factory.ConnectionString);
        await locker.OpenAsync();
        await using var tx = await locker.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
            "LOCK TABLE content.posts IN ACCESS EXCLUSIVE MODE", locker, tx))
            await lockCommand.ExecuteNonQueryAsync();

        try
        {
            using var response = await client.GetFeedAsync(a);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Bảng tin đang quá tải", json.RootElement.GetProperty("title").GetString());
            Assert.Equal(503, json.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
            Assert.DoesNotContain("feed.unavailable", json.RootElement.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    /// <summary><c>PAGE-04</c> — cursor rác → 400 <c>errors.cursor</c>, không âm thầm trả trang đầu.</summary>
    [Fact]
    public async Task PAGE_04_cursor_rac_tra_400_errors_cursor()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.GetFeedAsync(Guid.NewGuid(), "?cursor=rac");
        var problem = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal(400, problem.Status);
        Assert.Equal(["Cursor không hợp lệ."], problem.Errors["cursor"]);
    }
}
