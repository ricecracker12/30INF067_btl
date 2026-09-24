using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// B4 — <c>FEED-Q1</c> (Mục 10.2, L4): lưới N+1 ở đúng endpoint trọng điểm hiệu năng. Trang đầu trượt cache (harness mặc định,
/// Redis chết) với 20 bài có ảnh: số lệnh SQL là HẰNG SỐ viết tay theo Mục 7.2, và không đổi khi số nguồn tăng từ 50 lên 200.
/// k6 thấy N+1 muộn và mơ hồ; test này thấy sớm và chỉ đích danh câu thừa.
///
/// GĐ3 thêm lô <c>myReaction</c> vào <c>PostHydrator</c> thì hằng số tăng ĐÚNG 1 — sửa số và comment ở đây cùng commit đó.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedQueryCountTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    /// <summary>
    /// Mục 7.2, trượt cả hai tầng cache (Redis chết):
    /// nguồn 2 (bạn bè + theo dõi, SocialGraph đọc DB) + feed 1 (LATERAL) + ảnh 1 (một lô) + tác giả 1 (một lô)
    /// + myReaction 1 (một lô, GĐ3 Đ-3.11 — cuối <c>PostHydrator</c>) = 6.
    /// KHÔNG có "bài theo PK": trượt cache thì FeedService dùng luôn các dòng LATERAL (L14).
    /// </summary>
    private const int ExpectedStatements = 6;

    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Quan hệ bạn bè <c>accepted</c> giữa <paramref name="me"/> và từng id, INSERT bằng SQL chứ không qua API (L4): rate
    /// limit 100 req/phút theo người dùng — A gửi 200 lời mời là 429 từ lời mời thứ 101. Cặp chuẩn hóa bằng chính Postgres
    /// (<c>LEAST</c>/<c>GREATEST</c> trên <c>uuid</c>), không bằng C# — thứ tự <c>Guid</c> của .NET khác Postgres.
    /// </summary>
    private static Task<int> BefriendBySqlAsync(ModulesTestClient client, Guid me, IReadOnlyCollection<Guid> others) =>
        client.ExecuteSqlAsync(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
            select least($1, x), greatest($1, x), $1, 'accepted', now(), now(), now()
            from unnest($2::uuid[]) as x
            """,
            me, others.ToArray());

    [Fact]
    public async Task FEED_Q1_so_lenh_SQL_la_hang_so_6_o_50_va_200_nguon()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        await client.PutProfileOkAsync(a, new { displayName = "Người đọc Q1" });

        // 20 tác giả có hồ sơ, mỗi người một bài có ảnh qua API thật — ảnh để lô ảnh thật sự chạy.
        var authors = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var author = Guid.NewGuid();
            await client.PutProfileOkAsync(author, new { displayName = $"Tác giả {i}" });
            await client.CreatePostOkAsync(
                author, new { body = $"Bài {i}.", privacy = "friends", mediaKeys = new[] { client.PutPostObject(author) } });
            authors.Add(author);
        }

        // 50 nguồn: 20 tác giả + 30 người không bài.
        Assert.Equal(50, await BefriendBySqlAsync(
            client, a, [.. authors, .. Enumerable.Range(0, 30).Select(_ => Guid.NewGuid())]));

        using var counter = new SqlCommandCounter(factory.ConnectionString);

        // Làm nóng: cache quyền tầng 2, pool kết nối, model EF — không thuộc chi phí của feed.
        Assert.Equal(20, (await client.GetFeedOkAsync(a)).Items.Count);

        counter.Reset();
        var at50 = await client.GetFeedOkAsync(a);
        var n50 = counter.Statements.Count;
        var statements50 = string.Join(Environment.NewLine, counter.Statements);

        Assert.Equal(150, await BefriendBySqlAsync(client, a, [.. Enumerable.Range(0, 150).Select(_ => Guid.NewGuid())]));

        counter.Reset();
        var at200 = await client.GetFeedOkAsync(a);
        var n200 = counter.Statements.Count;

        Assert.Equal(20, at50.Items.Count);
        Assert.Equal(20, at200.Items.Count);
        Assert.True(n50 == ExpectedStatements, $"50 nguồn: {n50} lệnh, kỳ vọng {ExpectedStatements}:{Environment.NewLine}{statements50}");
        Assert.True(n200 == n50, $"200 nguồn: {n200} lệnh ≠ 50 nguồn {n50}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, counter.Statements));
    }
}
