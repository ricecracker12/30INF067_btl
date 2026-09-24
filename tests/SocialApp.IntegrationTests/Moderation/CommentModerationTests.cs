using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// Bước 9 của GĐ6 — báo cáo và ẩn BÌNH LUẬN qua API thật (FR-019 với bình luận, Đ-6.12–Đ-6.14): bình luận qua <c>content-v1</c>, báo cáo
/// qua <c>POST /reports</c>, quyết định qua <c>PATCH /reports/{id}</c>, khôi phục qua <c>POST /moderation/targets/comment/{id}/restore</c>,
/// đọc lại qua cây bình luận. Không dòng nào của Moderation đổi — chỉ thêm <c>CommentModerationTargets</c> ở Content (L-D13).
///
/// Bộ đếm soi bằng SQL: <c>posts.comment_count</c> trừ khi ẩn, cộng khi khôi phục; <c>reply_count</c> của cha không đổi (như xóa).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CommentModerationTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- dựng cảnh

    private static async Task<Guid> NguoiAsync(ModulesTestClient client, string ten)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = ten });
        return id;
    }

    private static async Task<Guid> BaiAsync(ModulesTestClient client, Guid author, string privacy = "public") =>
        (await client.CreatePostOkAsync(author, new { body = "Bài có bình luận.", privacy, mediaKeys = Array.Empty<object>() })).PostId;

    private static async Task<JsonElement> DocAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> BaoCaoAsync(ModulesTestClient client, Guid reporter, Guid comment, string reason = "harassment") =>
        await client.CreateReportAsync(reporter, new { targetType = "comment", targetId = comment, reasonCode = reason });

    private static async Task<Guid> BaoCaoOkAsync(ModulesTestClient client, Guid comment)
    {
        using var response = await BaoCaoAsync(client, Guid.NewGuid(), comment);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await DocAsync(response)).GetProperty("reportId").GetGuid();
    }

    private static async Task<HttpResponseMessage> GuiAsync(ModulesTestClient client, HttpMethod method, string path, object? body, string role)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), role);
        return await client.Http.SendAsync(request);
    }

    private static Task<HttpResponseMessage> AnAsync(ModulesTestClient client, Guid reportId) =>
        GuiAsync(client, HttpMethod.Patch, $"/api/v1/reports/{reportId}", new { decision = "hide" }, "MODERATOR");

    private static Task<HttpResponseMessage> KhoiPhucAsync(ModulesTestClient client, Guid comment) =>
        GuiAsync(client, HttpMethod.Post, $"/api/v1/moderation/targets/comment/{comment}/restore", null, "MODERATOR");

    private async Task<(string Status, int CommentCount)> TrongDbAsync(ModulesTestClient client, Guid comment, Guid post)
    {
        var status = (string)(await client.QueryRowAsync("select status from content.comments where comment_id = $1", comment))!["status"]!;
        var count = (int)(await client.QueryRowAsync("select comment_count from content.posts where post_id = $1", post))!["comment_count"]!;
        return (status, count);
    }

    // ---------------------------------------------------------------- báo cáo

    /// <summary>
    /// Đ-6.12 với bình luận: bình luận công khai của C → B báo được (201); C báo bình luận của chính mình → 400 <c>errors.targetId</c>;
    /// bình luận trong bài đã chuyển RIÊNG TƯ → 404 (thấy được mới báo được — không là máy dò, lỗ LEAK-01 của GĐ3); id lạ → 404 cùng thân.
    /// </summary>
    [Fact]
    public async Task CMT_REP_01_bao_cao_binh_luan_theo_luat_thay_duoc()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài CR");
        var c = await NguoiAsync(client, "Người bình luận CR");
        var p = await BaiAsync(client, a);
        var binhLuan = (await client.CreateCommentOkAsync(c, p, "Bình luận sẽ bị báo.")).CommentId;
        var sauRieng = (await client.CreateCommentOkAsync(c, p, "Bình luận trong bài sẽ riêng tư.")).CommentId;

        using (var ok = await BaoCaoAsync(client, Guid.NewGuid(), binhLuan))
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        using (var cuaMinh = await BaoCaoAsync(client, c, binhLuan))
        {
            var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(cuaMinh);
            Assert.Equal((400, true), (status, errors.ContainsKey("targetId")));
        }

        using (var rieng = await client.UpdatePostAsync(a, p, new { privacy = "private" }))
            Assert.Equal(HttpStatusCode.OK, rieng.StatusCode);
        using var khongThay = await BaoCaoAsync(client, Guid.NewGuid(), sauRieng);
        using var khongCo = await BaoCaoAsync(client, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (khongThay.StatusCode, khongCo.StatusCode));
        Assert.Equal((await DocAsync(khongThay)).GetProperty("detail").GetString(), (await DocAsync(khongCo)).GetProperty("detail").GetString());
    }

    // ---------------------------------------------------------------- ẩn, khôi phục

    /// <summary>
    /// US-019 AC-01 với bình luận: hai người báo bình luận của C → Moderator <c>hide</c> → bình luận <c>hidden</c>, cả hai báo cáo đóng;
    /// <c>comment_count</c> của bài trừ 1, <c>reply_count</c> của cha không đổi; trong cây bình luận nó còn chỗ nhưng không tác giả, không nội
    /// dung (như "đã xóa", Đ-6.14); chi tiết báo cáo cho Moderator thấy nội dung thật. C nhận thông báo <c>moderation</c> trỏ tới bình luận,
    /// kèm bài. Sau đó: trả lời vào nó → 400 <c>errors.parentId</c>; thả cảm xúc → 404; C xóa nó → 403 — bộ đếm không đổi thêm.
    /// </summary>
    [Fact]
    public async Task CMT_MOD_01_an_binh_luan_giu_cho_trong_cay_bo_dem_va_thong_bao()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài CM");
        var b = await NguoiAsync(client, "Người có bình luận cha");
        var c = await NguoiAsync(client, "Người bị ẩn bình luận");
        var p = await BaiAsync(client, a);
        var cha = (await client.CreateCommentOkAsync(b, p, "Bình luận cha.")).CommentId;
        var xau = (await client.CreateCommentOkAsync(c, p, "Nội dung xấu SECRET-cmt.", cha)).CommentId;
        var r1 = await BaoCaoOkAsync(client, xau);
        var r2 = await BaoCaoOkAsync(client, xau);
        var replyTruoc = (int)(await client.QueryRowAsync("select reply_count from content.comments where comment_id = $1", cha))!["reply_count"]!;
        Assert.Equal(("visible", 2), await TrongDbAsync(client, xau, p));

        using (var chiTiet = await GuiAsync(client, HttpMethod.Get, $"/api/v1/reports/{r1}", null, "MODERATOR"))
        {
            var target = (await DocAsync(chiTiet)).GetProperty("target");
            Assert.Equal(("comment", "published", "Nội dung xấu SECRET-cmt.", p), (
                target.GetProperty("type").GetString(), target.GetProperty("status").GetString(),
                target.GetProperty("body").GetString(), target.GetProperty("postId").GetGuid()));
        }

        using (var an = await AnAsync(client, r1))
        {
            var ketQua = await DocAsync(an);
            Assert.Equal(HttpStatusCode.OK, an.StatusCode);
            Assert.Equal("hidden", ketQua.GetProperty("targetStatus").GetString());
            Assert.Equal(new[] { r1, r2 }.Order(), ketQua.GetProperty("closedReportIds").EnumerateArray().Select(e => e.GetGuid()).Order());
        }

        Assert.Equal(("hidden", 1), await TrongDbAsync(client, xau, p));
        Assert.Equal(replyTruoc,
            (int)(await client.QueryRowAsync("select reply_count from content.comments where comment_id = $1", cha))!["reply_count"]!);

        var nhanh = (await client.ListRepliesOkAsync(Guid.NewGuid(), cha)).Items.Single(i => i.CommentId == xau);
        Assert.Equal(("hidden", null, null), (nhanh.Status.ToString().ToLowerInvariant(), (object?)nhanh.Author, nhanh.Body));

        await factory.DrainEventsAsync();
        var thongBao = await client.QueryRowAsync(
            "select type, target_type, target_id, post_id, last_actor_id, reason_code from notification.notifications where recipient_id = $1 " +
            "and type = 'moderation'", c);
        Assert.Equal(("comment", xau, p, (object?)null, "harassment"), (
            (string)thongBao!["target_type"]!, (Guid)thongBao["target_id"]!, (Guid)thongBao["post_id"]!,
            thongBao["last_actor_id"], (string)thongBao["reason_code"]!));

        using (var traLoi = await client.CreateCommentAsync(a, p, new { body = "Trả lời bình luận bị ẩn.", parentId = xau }))
        {
            var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(traLoi);
            Assert.Equal((400, true), (status, errors.ContainsKey("parentId")));
        }
        using (var camXuc = await client.ReactAsync(a, "comments", xau, "like"))
            Assert.Equal(HttpStatusCode.NotFound, camXuc.StatusCode);
        using (var xoa = await client.DeleteCommentAsync(c, xau))
            Assert.Equal(HttpStatusCode.Forbidden, xoa.StatusCode);
        Assert.Equal(("hidden", 1), await TrongDbAsync(client, xau, p));
    }

    /// <summary>
    /// Khôi phục bình luận (FR-020): 200 → <c>visible</c>, nội dung hiện lại, <c>comment_count</c> cộng lại. Lần hai → 409
    /// <c>moderation-not-hidden</c>, không đổi gì.
    /// </summary>
    [Fact]
    public async Task CMT_MOD_02_khoi_phuc_binh_luan_cong_lai_bo_dem()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài KP");
        var c = await NguoiAsync(client, "Người được khôi phục");
        var p = await BaiAsync(client, a);
        var binhLuan = (await client.CreateCommentOkAsync(c, p, "Bị ẩn nhầm.")).CommentId;
        using (var an = await AnAsync(client, await BaoCaoOkAsync(client, binhLuan)))
            Assert.Equal(HttpStatusCode.OK, an.StatusCode);
        Assert.Equal(("hidden", 0), await TrongDbAsync(client, binhLuan, p));

        using (var khoiPhuc = await KhoiPhucAsync(client, binhLuan))
            Assert.Equal(HttpStatusCode.OK, khoiPhuc.StatusCode);
        Assert.Equal(("visible", 1), await TrongDbAsync(client, binhLuan, p));
        var goc = (await client.ListCommentsOkAsync(Guid.NewGuid(), p)).Items.Single(i => i.CommentId == binhLuan);
        Assert.Equal("Bị ẩn nhầm.", goc.Body);

        using var lanHai = await KhoiPhucAsync(client, binhLuan);
        Assert.Equal(HttpStatusCode.Conflict, lanHai.StatusCode);
        Assert.Equal("urn:socialapp:problem:moderation-not-hidden", (await DocAsync(lanHai)).GetProperty("type").GetString());
        Assert.Equal(("visible", 1), await TrongDbAsync(client, binhLuan, p));
    }

    /// <summary>
    /// Đồng thời: Moderator <c>hide</c> ‖ tác giả XÓA cùng một bình luận, mười cặp song song. Mỗi bình luận kết thúc hoặc <c>hidden</c> hoặc
    /// <c>deleted</c>, và <c>comment_count</c> bị trừ ĐÚNG MỘT lần mỗi bình luận — hai đường cùng "chỉ đổi đúng 1 dòng mới trừ", cùng khóa
    /// bài trước (Đ-3.8). Không lượt nào 500 (deadlock <c>40P01</c> nếu thứ tự khóa ngược).
    /// </summary>
    [Fact]
    public async Task CMT_MOD_C1_an_va_xoa_dong_thoi_tru_bo_dem_dung_mot_lan()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài đua");
        var c = await NguoiAsync(client, "Người viết bị đua");
        var p = await BaiAsync(client, a);
        var cap = new List<(Guid Comment, Guid Report)>();
        for (var i = 0; i < 10; i++)
        {
            var binhLuan = (await client.CreateCommentOkAsync(c, p, $"Bình luận đua {i}.")).CommentId;
            cap.Add((binhLuan, await BaoCaoOkAsync(client, binhLuan)));
        }
        Assert.Equal(10, (await TrongDbAsync(client, cap[0].Comment, p)).CommentCount);

        var ketQua = await Task.WhenAll(cap.SelectMany(x => new[]
        {
            Task.Run(async () => { using var r = await AnAsync(client, x.Report); return r.StatusCode; }),
            Task.Run(async () => { using var r = await client.DeleteCommentAsync(c, x.Comment); return r.StatusCode; }),
        }));

        Assert.DoesNotContain(HttpStatusCode.InternalServerError, ketQua);
        Assert.Equal(0, (await TrongDbAsync(client, cap[0].Comment, p)).CommentCount);
        foreach (var (binhLuan, _) in cap)
            Assert.Contains((await TrongDbAsync(client, binhLuan, p)).Status, new[] { "hidden", "deleted" });
    }
}
