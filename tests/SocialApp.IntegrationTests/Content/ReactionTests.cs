using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D5–D7 GĐ3 — nghiệm thu cảm xúc (Mục 10.1 <c>REACT-01..08</c>): bốn nhánh của Đ-3.7/Mục 7.4 trên bài và bình luận, bộ đếm
/// <c>jsonb</c> không bao giờ còn khóa 0, không chạm <c>updated_at</c>/<c>edited_at</c>, <c>myReaction</c> theo người xem, và
/// event <c>ReactionSet</c> chỉ khi thả mới / đổi loại.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReactionTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        factory.UseTestServices(services =>
        {
            services.AddSingleton<Recorded>();
            services.AddIntegrationEventHandler<ReactionSet, RecordingHandler>();
        });
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ModulesTestClient Client, Guid Author, Guid PostId)> ArrangeAsync()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Chủ bài" });
        var post = await client.CreatePostOkAsync(author, new { body = "Thả tim đi.", privacy = "public", mediaKeys = Array.Empty<object>() });
        return (client, author, post.PostId);
    }

    private static async Task<int> RowCountAsync(ModulesTestClient client, Guid userId, Guid targetId) =>
        Convert.ToInt32((await client.QueryRowAsync(
            "select count(*) from content.reactions where user_id = $1 and target_id = $2", userId, targetId))!.Values.Single());

    [Fact]
    public async Task REACT_01_PUT_like_lan_dau()
    {
        var (client, _, postId) = await ArrangeAsync();
        var me = Guid.NewGuid();

        var summary = await client.ReactOkAsync(me, "posts", postId, "like");

        Assert.Equal(new Dictionary<string, int> { ["like"] = 1 }, summary.ReactionCounts);
        Assert.Equal(ReactionType.Like, summary.MyReaction);
    }

    /// <summary>BR-05 — đổi loại là MỘT dòng, và loại cũ về 0 thì mất khóa.</summary>
    [Fact]
    public async Task REACT_02_like_roi_love_mot_dong_khong_con_khoa_like()
    {
        var (client, _, postId) = await ArrangeAsync();
        var me = Guid.NewGuid();

        await client.ReactOkAsync(me, "posts", postId, "like");
        var summary = await client.ReactOkAsync(me, "posts", postId, "love");

        Assert.Equal(new Dictionary<string, int> { ["love"] = 1 }, summary.ReactionCounts);
        Assert.Equal(1, await RowCountAsync(client, me, postId));
    }

    [Fact]
    public async Task REACT_03_PUT_like_hai_lan_idempotent()
    {
        var (client, _, postId) = await ArrangeAsync();
        var me = Guid.NewGuid();

        await client.ReactOkAsync(me, "posts", postId, "like");
        var summary = await client.ReactOkAsync(me, "posts", postId, "like");

        Assert.Equal(new Dictionary<string, int> { ["like"] = 1 }, summary.ReactionCounts);
        Assert.Equal(1, await RowCountAsync(client, me, postId));
    }

    [Fact]
    public async Task REACT_04_DELETE_khi_chua_tha_van_200_tom_tat_hien_tai()
    {
        var (client, _, postId) = await ArrangeAsync();
        var other = Guid.NewGuid();
        await client.ReactOkAsync(other, "posts", postId, "wow");

        var summary = await client.ReactOkAsync(Guid.NewGuid(), "posts", postId, type: null);

        Assert.Equal(new Dictionary<string, int> { ["wow"] = 1 }, summary.ReactionCounts);
        Assert.Null(summary.MyReaction);
    }

    /// <summary>Đ-3.8 — về 0 thì XÓA KHÓA: <c>{}</c>, không phải <c>{"like":0}</c>. Kiểm cả trên dây lẫn trong cột.</summary>
    [Fact]
    public async Task REACT_05_PUT_roi_DELETE_la_object_rong()
    {
        var (client, _, postId) = await ArrangeAsync();
        var me = Guid.NewGuid();

        await client.ReactOkAsync(me, "posts", postId, "like");
        var summary = await client.ReactOkAsync(me, "posts", postId, type: null);

        Assert.Empty(summary.ReactionCounts);
        Assert.Null(summary.MyReaction);
        Assert.Equal("{}", (await client.QueryRowAsync(
            "select reaction_counts::text as c from content.posts where post_id = $1", postId))!["c"]);
    }

    [Fact]
    public async Task REACT_06_cam_xuc_tren_binh_luan_da_xoa_la_404()
    {
        var (client, author, postId) = await ArrangeAsync();
        var comment = await client.CreateCommentOkAsync(author, postId, "Sẽ xóa");
        var before = await client.ReactOkAsync(Guid.NewGuid(), "comments", comment.CommentId, "haha");
        Assert.Equal(1, before.ReactionCounts["haha"]);

        using var _ = await client.DeleteCommentAsync(author, comment.CommentId);
        using var put = await client.ReactAsync(Guid.NewGuid(), "comments", comment.CommentId, "haha");
        using var delete = await client.ReactAsync(Guid.NewGuid(), "comments", comment.CommentId, null);

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    /// <summary>Đ-3.8 — bộ đếm là thống kê, không phải nội dung: không chạm <c>updated_at</c>, không làm bài hiện "đã chỉnh sửa".</summary>
    [Fact]
    public async Task REACT_07_tha_cam_xuc_khong_doi_updated_at_va_edited_at()
    {
        var (client, author, postId) = await ArrangeAsync();
        var comment = await client.CreateCommentOkAsync(author, postId, "Bình luận");
        const string PostSql = "select updated_at::text as u, coalesce(edited_at::text, '') as e from content.posts where post_id = $1";
        const string CommentSql = "select updated_at::text as u from content.comments where comment_id = $1";
        var postBefore = await client.QueryRowAsync(PostSql, postId);
        var commentBefore = await client.QueryRowAsync(CommentSql, comment.CommentId);

        var me = Guid.NewGuid();
        await client.ReactOkAsync(me, "posts", postId, "like");
        await client.ReactOkAsync(me, "posts", postId, "sad");
        await client.ReactOkAsync(me, "comments", comment.CommentId, "love");

        Assert.Equal(postBefore, await client.QueryRowAsync(PostSql, postId));
        Assert.Equal(commentBefore, await client.QueryRowAsync(CommentSql, comment.CommentId));
    }

    /// <summary>
    /// Đ-3.10/3.11 — <c>myReaction</c> là trường THEO NGƯỜI XEM, ở mọi đường trả <c>PostResponse</c>: một bài, trang cá nhân, feed.
    /// </summary>
    [Fact]
    public async Task REACT_08_myReaction_dung_cho_nguoi_tha_null_cho_nguoi_khac()
    {
        var (client, author, postId) = await ArrangeAsync();
        var me = Guid.NewGuid();
        await client.ReactOkAsync(me, "posts", postId, "angry");

        using var mine = await client.GetPostAsync(me, postId);
        using var theirs = await client.GetPostAsync(author, postId);
        var listMine = await client.ListPostsOkAsync(me, author);
        var listTheirs = await client.ListPostsOkAsync(author, author);

        Assert.Equal(ReactionType.Angry, (await mine.Content.ReadFromJsonAsync<PostResponse>(ModulesTestClient.Json))!.MyReaction);
        Assert.Null((await theirs.Content.ReadFromJsonAsync<PostResponse>(ModulesTestClient.Json))!.MyReaction);
        Assert.Equal(ReactionType.Angry, Assert.Single(listMine.Items).MyReaction);
        Assert.Null(Assert.Single(listTheirs.Items).MyReaction);

        // Bình luận cũng vậy.
        var comment = await client.CreateCommentOkAsync(author, postId, "Có cảm xúc");
        await client.ReactOkAsync(me, "comments", comment.CommentId, "love");
        Assert.Equal(ReactionType.Love, Assert.Single((await client.ListCommentsOkAsync(me, postId)).Items).MyReaction);
        Assert.Null(Assert.Single((await client.ListCommentsOkAsync(author, postId)).Items).MyReaction);
    }

    [Theory]
    [InlineData("{\"type\":\"heart\"}")]
    [InlineData("{\"type\":99}")]
    [InlineData("{}")]
    public async Task Loai_cam_xuc_sai_la_400(string json)
    {
        var (client, _, postId) = await ArrangeAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/posts/{postId}/reactions/me")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());

        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Đ-3.12 — chỉ thả MỚI (<c>isNew</c>) và ĐỔI loại phát event; gỡ và lặp lại cùng loại thì không.</summary>
    [Fact]
    public async Task ReactionSet_chi_phat_khi_tha_moi_hoac_doi_loai()
    {
        var (client, author, postId) = await ArrangeAsync();
        var comment = await client.CreateCommentOkAsync(author, postId, "Của chủ bài");
        var me = Guid.NewGuid();

        await client.ReactOkAsync(me, "posts", postId, "like");       // mới → isNew
        await client.ReactOkAsync(me, "posts", postId, "like");       // giống → không
        await client.ReactOkAsync(me, "posts", postId, "love");       // đổi → isNew=false
        await client.ReactOkAsync(me, "posts", postId, null);         // gỡ → không
        await client.ReactOkAsync(me, "comments", comment.CommentId, "wow");
        await factory.DrainEventsAsync();

        var events = factory.Services.GetRequiredService<Recorded>().Events.Where(e => e.ActorId == me).ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal([true, false], events.Where(e => e.TargetType == ReactionTargetKind.Post).Select(e => e.IsNew));
        Assert.All(events, e => Assert.Equal(author, e.TargetAuthorId));
        var onComment = Assert.Single(events, e => e.TargetType == ReactionTargetKind.Comment);
        Assert.Equal((comment.CommentId, postId), (onComment.TargetId, onComment.PostId));
    }

    private sealed class Recorded
    {
        public ConcurrentQueue<ReactionSet> Events { get; } = new();
    }

    private sealed class RecordingHandler(Recorded recorded) : IIntegrationEventHandler<ReactionSet>
    {
        public Task HandleAsync(ReactionSet integrationEvent, CancellationToken ct)
        {
            recorded.Events.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
