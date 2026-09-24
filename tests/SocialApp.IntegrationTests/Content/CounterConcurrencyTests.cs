using System.Net;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// B4 GĐ3 — <c>COUNT-01..04</c> (Mục 10.2): thứ DUY NHẤT chứng minh Đ-3.8. Request song song thật qua <c>HttpClient</c> của
/// <c>WebApplicationFactory</c> (<c>Task.WhenAll</c>), Postgres thật; bộ đếm so với <c>GROUP BY</c> trên bảng gốc bằng chính các câu
/// đối soát của Mục 12 — lệch một đơn vị là lỗi không bao giờ tự lành.
///
/// Mỗi test một bài mới (bộ đếm theo bài nên không lẫn nhau). Mỗi người gọi dưới 100 request (hạn mức theo user).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CounterConcurrencyTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Mục 12: bài có <c>comment_count</c> hay <c>reaction_counts</c> lệch bảng gốc. Rỗng = khớp.</summary>
    private const string PostDriftSql =
        """
        select count(*) from (
            select p.post_id from content.posts p
            left join content.comments c on c.post_id = p.post_id and c.status = 'visible'
            where p.post_id = $1
            group by p.post_id, p.comment_count having p.comment_count <> count(c.comment_id)
            union all
            select p.post_id from content.posts p
            where p.post_id = $1 and p.reaction_counts <> coalesce((
                select jsonb_object_agg(type, n) from (
                    select type, count(*) as n from content.reactions
                    where target_type = 'post' and target_id = p.post_id group by type) t), '{}'::jsonb)
        ) drift
        """;

    /// <summary>Mục 12, bản cho bình luận của một bài: <c>reply_count</c> (mọi trạng thái) và <c>reaction_counts</c>.</summary>
    private const string CommentDriftSql =
        """
        select count(*) from content.comments c
        where c.post_id = $1 and (
            c.reply_count <> (select count(*) from content.comments r where r.parent_id = c.comment_id)
            or c.reaction_counts <> coalesce((
                select jsonb_object_agg(type, n) from (
                    select type, count(*) as n from content.reactions
                    where target_type = 'comment' and target_id = c.comment_id group by type) t), '{}'::jsonb))
        """;

    private async Task<(ModulesTestClient Client, Guid Author, Guid PostId)> ArrangeAsync()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Bài nóng" });
        var post = await client.CreatePostOkAsync(author, new { body = "Cả trăm người bấm.", privacy = "public", mediaKeys = Array.Empty<object>() });
        return (client, author, post.PostId);
    }

    private static async Task<long> ScalarAsync(ModulesTestClient client, string sql, params object[] args) =>
        Convert.ToInt64((await client.QueryRowAsync(sql, args))!.Values.Single());

    private static async Task AssertNoDriftAsync(ModulesTestClient client, Guid postId)
    {
        Assert.Equal(0, await ScalarAsync(client, PostDriftSql, postId));
        Assert.Equal(0, await ScalarAsync(client, CommentDriftSql, postId));
    }

    private static async Task<Guid[]> UsersAsync(ModulesTestClient client, int count)
    {
        var users = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        await Task.WhenAll(users.Select(u => client.PutProfileOkAsync(u, new { displayName = "Người bấm" })));
        return users;
    }

    /// <summary>
    /// COUNT-01 — 50 người khác nhau cùng thả like vào một bài. Bản "nạp dictionary, ++, SaveChanges" mất cập nhật ngay ở đây:
    /// hai request cùng đọc like = 5, cùng ghi 6.
    /// </summary>
    [Fact]
    public async Task COUNT_01_nam_muoi_nguoi_tha_like_song_song()
    {
        var (client, _, postId) = await ArrangeAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => client.ReactAsync(Guid.NewGuid(), "posts", postId, "like")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(50, await ScalarAsync(client, "select (reaction_counts->>'like')::int from content.posts where post_id = $1", postId));
        Assert.Equal(50, await ScalarAsync(client, "select count(*) from content.reactions where target_id = $1", postId));
        await AssertNoDriftAsync(client, postId);
    }

    /// <summary>
    /// COUNT-02 — MỘT người, 10 request PUT song song với loại ngẫu nhiên (hai tab cùng bấm). Không khóa dòng đối tượng thì nhiều
    /// request cùng thấy "chưa có", cùng INSERT, và PK ba cột nổ 23505 thành 500.
    /// </summary>
    [Fact]
    public async Task COUNT_02_mot_nguoi_muoi_request_song_song_mot_dong_khong_500()
    {
        var (client, _, postId) = await ArrangeAsync();
        var me = Guid.NewGuid();
        string[] types = ["like", "love", "haha", "wow", "sad", "angry"];

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => client.ReactAsync(me, "posts", postId, types[Random.Shared.Next(types.Length)])));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await ScalarAsync(client, "select count(*) from content.reactions where user_id = $1 and target_id = $2", me, postId));
        Assert.Equal(1, await ScalarAsync(client,
            "select coalesce(sum(value::int), 0) from content.posts, jsonb_each_text(reaction_counts) where post_id = $1", postId));
        await AssertNoDriftAsync(client, postId);
    }

    /// <summary>COUNT-03 — 30 bình luận mới song song với 10 lần xóa song song: <c>comment_count</c> = số dòng visible.</summary>
    [Fact]
    public async Task COUNT_03_ba_muoi_binh_luan_va_muoi_lan_xoa_song_song()
    {
        var (client, _, postId) = await ArrangeAsync();
        var writers = await UsersAsync(client, 30);
        var deleters = await UsersAsync(client, 10);
        var toDelete = await Task.WhenAll(deleters.Select(u => client.CreateCommentOkAsync(u, postId, "Sẽ xóa")));

        var creates = writers.Select(u => client.CreateCommentAsync(u, postId, new { body = "Song song" }));
        var deletes = toDelete.Select(c => client.DeleteCommentAsync(c.Author!.UserId, c.CommentId));
        var responses = await Task.WhenAll(creates.Concat(deletes));

        Assert.Equal(30, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(30, await ScalarAsync(client, "select comment_count from content.posts where post_id = $1", postId));
        await AssertNoDriftAsync(client, postId);
    }

    /// <summary>
    /// COUNT-04 — năm phản hồi và một lần xóa CHA song song, 20 vòng. Chứng minh thứ tự khóa BÀI → BÌNH LUẬN (Đ-3.8): viết ngược ở
    /// một hàm là deadlock <c>40P01</c> → 500. Phản hồi thắng cuộc đua thì 201, thua thì 400 <c>parentId</c> — cả hai đều hợp lệ.
    ///
    /// Năm chứ không một: bản một-phản-hồi-mỗi-vòng KHÔNG tái hiện được deadlock khi đảo thứ tự khóa trong <c>SoftDeleteAsync</c> (hai
    /// lần chạy đều xanh — cửa sổ quá hẹp). Năm phản hồi xếp hàng trên khóa bài cho lần xóa chen vào giữa: đột biến đỏ 2/2 lần.
    /// </summary>
    [Fact]
    public async Task COUNT_04_tao_phan_hoi_va_xoa_cha_song_song_khong_deadlock()
    {
        var (client, author, postId) = await ArrangeAsync();
        // Năm người trả lời CÙNG LÚC với lần xóa, mỗi vòng — một người một phản hồi/vòng để không ai chạm hạn mức 100 req/phút.
        var repliers = await UsersAsync(client, 5);

        for (var round = 0; round < 20; round++)
        {
            var parent = await client.CreateCommentOkAsync(author, postId, $"Cha vòng {round}");

            var replies = repliers.Select(r => client.CreateCommentAsync(r, postId, new { body = "Trả lời", parentId = parent.CommentId }));
            var responses = await Task.WhenAll(replies.Append(client.DeleteCommentAsync(author, parent.CommentId)));

            Assert.All(responses[..^1], r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }));
            Assert.Equal(HttpStatusCode.NoContent, responses[^1].StatusCode);
        }

        await AssertNoDriftAsync(client, postId);
    }
}
