using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Comments;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D1–D4 GĐ3 — nghiệm thu chức năng của bình luận (Mục 10.1 <c>CMT-01..10</c>) qua HTTP thật, Postgres thật, cộng hai trường hợp
/// biên của Đ-3.3 và event sau COMMIT (Đ-3.12). Bộ đếm kiểm bằng SQL trên bảng gốc, không tin con số API trả.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CommentTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        factory.UseTestServices(services =>
        {
            services.AddSingleton<Recorded>();
            services.AddIntegrationEventHandler<CommentCreated, RecordingHandler>();
        });
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ModulesTestClient Client, Guid Author, Guid PostId)> ArrangeAsync(string privacy = "public")
    {
        var client = new ModulesTestClient(factory);
        var author = await UserAsync(client, "Chủ bài");
        var post = await client.CreatePostOkAsync(author, new { body = "Bài để bình luận.", privacy, mediaKeys = Array.Empty<object>() });
        return (client, author, post.PostId);
    }

    private static async Task<Guid> UserAsync(ModulesTestClient client, string name)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = name });
        return id;
    }

    private static async Task<int> ScalarAsync(ModulesTestClient client, string sql, Guid id) =>
        Convert.ToInt32((await client.QueryRowAsync(sql, id))!.Values.Single());

    private static Task<int> CommentCountAsync(ModulesTestClient client, Guid postId) =>
        ScalarAsync(client, "select comment_count from content.posts where post_id = $1", postId);

    private static Task<int> ReplyCountAsync(ModulesTestClient client, Guid commentId) =>
        ScalarAsync(client, "select reply_count from content.comments where comment_id = $1", commentId);

    [Fact]
    public async Task CMT_01_binh_luan_goc_vao_bai_public_cua_nguoi_khac()
    {
        var (client, author, postId) = await ArrangeAsync();
        var reader = await UserAsync(client, "Người đọc");

        var comment = await client.CreateCommentOkAsync(reader, postId, "Đẹp quá!");

        Assert.Equal(1, comment.Depth);
        Assert.Null(comment.ParentId);
        Assert.Equal(CommentStatus.Visible, comment.Status);
        Assert.Equal("Đẹp quá!", comment.Body);
        Assert.Equal(reader, comment.Author!.UserId);
        Assert.True(comment.CanDelete);
        Assert.Empty(comment.ReactionCounts);
        Assert.Equal(1, await CommentCountAsync(client, postId));

        // Chủ bài đọc: cùng bình luận, nhưng canDelete là của NGƯỜI GỌI (Đ-3.2: chủ bài không xóa bình luận người khác).
        var page = await client.ListCommentsOkAsync(author, postId);
        Assert.False(Assert.Single(page.Items).CanDelete);
        using var post = await client.GetPostAsync(author, postId);
        Assert.Equal(1, (await post.Content.ReadFromJsonAsync<Modules.Content.Application.Posts.PostResponse>(ModulesTestClient.Json))!.CommentCount);
    }

    [Fact]
    public async Task CMT_02_phan_hoi_cap_2_roi_cap_3()
    {
        var (client, author, postId) = await ArrangeAsync();

        var root = await client.CreateCommentOkAsync(author, postId, "Cấp 1");
        var level2 = await client.CreateCommentOkAsync(author, postId, "Cấp 2", root.CommentId);
        var level3 = await client.CreateCommentOkAsync(author, postId, "Cấp 3", level2.CommentId);

        Assert.Equal((2, 3), (level2.Depth, level3.Depth));
        Assert.Equal(root.CommentId, level2.ParentId);
        Assert.Equal(1, await ReplyCountAsync(client, root.CommentId));
        Assert.Equal(1, await ReplyCountAsync(client, level2.CommentId));
        Assert.Equal(3, await CommentCountAsync(client, postId));   // mọi cấp (Đ-3.5)

        // Tải lười (Đ-3.6): trang gốc chỉ có cấp 1; phản hồi của từng cha tải riêng.
        Assert.Single((await client.ListCommentsOkAsync(author, postId)).Items);
        Assert.Equal(level2.CommentId, Assert.Single((await client.ListRepliesOkAsync(author, root.CommentId)).Items).CommentId);
        Assert.Equal(level3.CommentId, Assert.Single((await client.ListRepliesOkAsync(author, level2.CommentId)).Items).CommentId);
    }

    [Fact]
    public async Task CMT_03_tra_loi_cap_3_bi_chan_BR08()
    {
        var (client, author, postId) = await ArrangeAsync();
        var root = await client.CreateCommentOkAsync(author, postId, "1");
        var level2 = await client.CreateCommentOkAsync(author, postId, "2", root.CommentId);
        var level3 = await client.CreateCommentOkAsync(author, postId, "3", level2.CommentId);

        using var response = await client.CreateCommentAsync(author, postId, new { body = "4", parentId = level3.CommentId });

        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Equal([CommentDepthPolicy.TooDeep], errors["parentId"]);
        Assert.Equal(3, await CommentCountAsync(client, postId));   // không có dòng mới
        Assert.Equal(0, await ReplyCountAsync(client, level3.CommentId));
    }

    [Fact]
    public async Task CMT_04_parentId_cua_bai_khac_thi_cung_cau_khong_con_ton_tai()
    {
        var (client, author, postId) = await ArrangeAsync();
        var (_, _, otherPostId) = await ArrangeAsync();
        var foreign = await client.CreateCommentOkAsync(author, otherPostId, "Ở bài khác");

        using var otherPost = await client.CreateCommentAsync(author, postId, new { body = "x", parentId = foreign.CommentId });
        using var missing = await client.CreateCommentAsync(author, postId, new { body = "x", parentId = Guid.NewGuid() });

        foreach (var response in new[] { otherPost, missing })
        {
            var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
            Assert.Equal(400, status);
            Assert.Equal([CommentDepthPolicy.ParentGone], errors["parentId"]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    [InlineData(null)]
    public async Task CMT_05_body_rong_hoac_toan_khoang_trang(string? body)
    {
        var (client, author, postId) = await ArrangeAsync();

        using var response = await client.CreateCommentAsync(author, postId, new { body });

        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Equal([CommentPolicy.Empty], errors["body"]);
    }

    [Fact]
    public async Task CMT_05_body_1001_ky_tu()
    {
        var (client, author, postId) = await ArrangeAsync();

        using var ok = await client.CreateCommentAsync(author, postId, new { body = new string('a', 1000) });
        using var tooLong = await client.CreateCommentAsync(author, postId, new { body = new string('a', 1001) });

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(tooLong);
        Assert.Equal(400, status);
        Assert.Equal([CommentPolicy.BodyTooLong], errors["body"]);
    }

    /// <summary>Đ-3.5 — xóa giữ nhánh: dòng vẫn ở đúng chỗ, không lộ ai viết/viết gì, "Xem 2 phản hồi" vẫn còn.</summary>
    [Fact]
    public async Task CMT_06_xoa_binh_luan_co_hai_phan_hoi_nhanh_van_con()
    {
        var (client, author, postId) = await ArrangeAsync();
        var replier = await UserAsync(client, "Người trả lời");
        var root = await client.CreateCommentOkAsync(author, postId, "Sẽ bị xóa");
        await client.CreateCommentOkAsync(replier, postId, "Phản hồi 1", root.CommentId);
        await client.CreateCommentOkAsync(replier, postId, "Phản hồi 2", root.CommentId);

        using var deleted = await client.DeleteCommentAsync(author, root.CommentId);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var row = Assert.Single((await client.ListCommentsOkAsync(replier, postId)).Items);
        Assert.Equal(CommentStatus.Deleted, row.Status);
        Assert.Null(row.Body);
        Assert.Null(row.Author);
        Assert.Equal(2, row.ReplyCount);
        Assert.False(row.CanDelete);
        Assert.Equal(2, (await client.ListRepliesOkAsync(replier, root.CommentId)).Items.Count);
        Assert.Equal(2, await CommentCountAsync(client, postId));   // chỉ đếm visible

        // Nội dung vẫn nằm trong DB (bằng chứng cho báo cáo vi phạm ở GĐ6), chỉ không ra khỏi server.
        Assert.Equal(1, await ScalarAsync(client,
            "select count(*) from content.comments where comment_id = $1 and body = 'Sẽ bị xóa' and deleted_at is not null",
            root.CommentId));
    }

    [Fact]
    public async Task CMT_07_xoa_lan_hai_va_xoa_cua_nguoi_khac_cung_403()
    {
        var (client, author, postId) = await ArrangeAsync();
        var comment = await client.CreateCommentOkAsync(author, postId, "Một lần thôi");
        var stranger = Guid.NewGuid();

        using var notMine = await client.DeleteCommentAsync(stranger, comment.CommentId);
        using var first = await client.DeleteCommentAsync(author, comment.CommentId);
        using var second = await client.DeleteCommentAsync(author, comment.CommentId);
        using var missing = await client.DeleteCommentAsync(author, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, notMine.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(0, await CommentCountAsync(client, postId));   // trừ đúng MỘT lần
    }

    [Fact]
    public async Task CMT_08_tra_loi_binh_luan_da_xoa()
    {
        var (client, author, postId) = await ArrangeAsync();
        var root = await client.CreateCommentOkAsync(author, postId, "Gốc");
        using var _ = await client.DeleteCommentAsync(author, root.CommentId);

        using var response = await client.CreateCommentAsync(author, postId, new { body = "Muộn rồi", parentId = root.CommentId });

        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Equal([CommentDepthPolicy.ParentGone], errors["parentId"]);
    }

    /// <summary>Keyset ASC (Đ-3.6): cũ trước; trang 2 đủ phần còn lại; không nhân đôi, không nhảy cóc.</summary>
    [Fact]
    public async Task CMT_09_25_binh_luan_limit_20_hai_trang_cu_truoc()
    {
        var (client, author, postId) = await ArrangeAsync();
        var created = new List<Guid>();
        for (var i = 0; i < 25; i++)
            created.Add((await client.CreateCommentOkAsync(author, postId, $"Bình luận {i}")).CommentId);

        var first = await client.ListCommentsOkAsync(author, postId, "?limit=20");
        Assert.Equal(20, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await client.ListCommentsOkAsync(author, postId, $"?limit=20&cursor={first.NextCursor}");
        Assert.Equal(5, second.Items.Count);
        Assert.Null(second.NextCursor);

        Assert.Equal(created, [.. first.Items.Concat(second.Items).Select(c => c.CommentId)]);
    }

    [Fact]
    public async Task CMT_09b_cursor_rac_va_limit_ngoai_mien_la_400()
    {
        var (client, author, postId) = await ArrangeAsync();

        using var cursor = await client.ListCommentsAsync(author, postId, "?cursor=rac");
        using var limit = await client.ListCommentsAsync(author, postId, "?limit=51");

        Assert.Contains("cursor", (await ModulesTestClient.ReadProblemAsync(cursor)).Errors.Keys);
        Assert.Contains("limit", (await ModulesTestClient.ReadProblemAsync(limit)).Errors.Keys);
    }

    [Fact]
    public async Task CMT_10_chua_co_ho_so_ma_binh_luan_la_403()
    {
        var (client, _, postId) = await ArrangeAsync();

        using var response = await client.CreateCommentAsync(Guid.NewGuid(), postId, new { body = "Tôi chưa có hồ sơ" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CommentCountAsync(client, postId));
    }

    /// <summary>Đ-3.3 biên 1 — tác giả bình luận vẫn xóa được bình luận của mình khi bài giờ đã private với họ.</summary>
    [Fact]
    public async Task Xoa_binh_luan_cua_minh_khi_bai_da_private_voi_minh_van_duoc()
    {
        var (client, author, postId) = await ArrangeAsync();
        var commenter = await UserAsync(client, "Người bình luận");
        var comment = await client.CreateCommentOkAsync(commenter, postId, "Trước khi bài đóng lại");
        await client.UpdatePostOkAsync(author, postId, new { privacy = "private" });

        using var read = await client.ListCommentsAsync(commenter, postId);
        using var delete = await client.DeleteCommentAsync(commenter, comment.CommentId);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);      // không còn xem được
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);   // nhưng xóa được dữ liệu của mình
    }

    /// <summary>
    /// Đ-3.3 biên 2 + quy ước 3b — bài đã xóa mềm: đọc/viết → 404; và "không có" với "không được xem" trả CÙNG body ở cả hai
    /// đường (qua <c>postId</c> và qua <c>commentId</c>).
    /// </summary>
    [Fact]
    public async Task Bai_da_xoa_va_khong_duoc_xem_cung_mot_404()
    {
        var (client, author, postId) = await ArrangeAsync();
        var comment = await client.CreateCommentOkAsync(author, postId, "Trong bài sắp xóa");
        var reader = await UserAsync(client, "Người đọc");
        using var _ = await client.DeletePostAsync(author, postId);

        using var list = await client.ListCommentsAsync(reader, postId);
        using var create = await client.CreateCommentAsync(reader, postId, new { body = "x" });
        using var replies = await client.ListRepliesAsync(reader, comment.CommentId);
        using var noSuchComment = await client.ListRepliesAsync(reader, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, replies.StatusCode);

        var (_, t1, _) = await ModulesTestClient.ReadProblemAsync(replies);
        var (_, t2, _) = await ModulesTestClient.ReadProblemAsync(noSuchComment);
        Assert.Equal(t1, t2);
    }

    /// <summary>
    /// GĐ4 bàn giao — bài <c>friends</c> giữa hai người là BẠN THẬT (API kết bạn): bạn đọc và viết được; người lạ thì 404.
    /// </summary>
    [Fact]
    public async Task Bai_friends_ban_that_thay_nguoi_la_khong()
    {
        var (client, author, postId) = await ArrangeAsync("friends");
        var friend = await UserAsync(client, "Bạn thật");
        await client.MakeFriendsAsync(friend, author);
        var stranger = await UserAsync(client, "Người lạ");

        var comment = await client.CreateCommentOkAsync(friend, postId, "Bạn bè mới thấy");
        using var strangerRead = await client.ListRepliesAsync(stranger, comment.CommentId);

        Assert.Single((await client.ListCommentsOkAsync(friend, postId)).Items);
        Assert.Equal(HttpStatusCode.NotFound, strangerRead.StatusCode);
    }

    /// <summary>Đ-3.12 — event SAU COMMIT, đúng vai từng id; tạo bị từ chối thì không event.</summary>
    [Fact]
    public async Task Tao_binh_luan_va_phan_hoi_phat_CommentCreated_dung_nguoi_nhan()
    {
        var (client, author, postId) = await ArrangeAsync();
        var replier = await UserAsync(client, "Người trả lời");

        var root = await client.CreateCommentOkAsync(author, postId, "Gốc");
        var reply = await client.CreateCommentOkAsync(replier, postId, "Trả lời", root.CommentId);
        using var rejected = await client.CreateCommentAsync(replier, postId, new { body = "" });
        await factory.DrainEventsAsync();

        var events = factory.Services.GetRequiredService<Recorded>().Events.Where(e => e.PostId == postId).ToList();
        Assert.Equal(2, events.Count);

        var first = events.Single(e => e.CommentId == root.CommentId);
        Assert.Equal((author, author, (Guid?)null, (Guid?)null), (first.PostAuthorId, first.ActorId, first.ParentCommentId, first.ParentAuthorId));

        var second = events.Single(e => e.CommentId == reply.CommentId);
        Assert.Equal(author, second.PostAuthorId);
        Assert.Equal(replier, second.ActorId);
        Assert.Equal(root.CommentId, second.ParentCommentId);
        Assert.Equal(author, second.ParentAuthorId);
        Assert.Empty(second.MentionedUserIds);
    }

    private sealed class Recorded
    {
        public ConcurrentQueue<CommentCreated> Events { get; } = new();
    }

    private sealed class RecordingHandler(Recorded recorded) : IIntegrationEventHandler<CommentCreated>
    {
        public Task HandleAsync(CommentCreated integrationEvent, CancellationToken ct)
        {
            recorded.Events.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
