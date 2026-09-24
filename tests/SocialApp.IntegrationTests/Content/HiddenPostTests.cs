using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Moderation.Domain;
using SocialApp.Modules.Moderation.Infrastructure;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// GĐ6 D7a — BR-07 phía người đọc (Đ-6.14) qua API thật: bài <c>hidden</c> tác giả thấy kèm lý do, người khác (kể cả bạn bè,
/// Moderator, Admin) thấy như bài không tồn tại, tác giả không sửa được mà xóa được.
///
/// Bài bị ẩn bằng <see cref="IModerationTargets.HideAsync"/> trong một transaction của Moderation — đúng hợp đồng ghi C2 mà
/// <c>PATCH /reports</c> (D7c) sẽ gọi, không SQL tay: <c>hiddenAt</c> là <c>updated_at</c> do CHÍNH <c>HideAsync</c> đóng dấu, và
/// SQL tay đặt mốc khác thì ca <c>HID-01</c> không chứng minh được gì về mốc đó.
///
/// Hai lỗ BR-07 mà ca này đóng (L-D4): trước D7a <c>GET</c> bài <c>hidden</c> trả 200 cho mọi người qua BR-02, và <c>PATCH</c>
/// sửa được bài <c>hidden</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HiddenPostTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> NguoiCoHoSoAsync(ModulesTestClient client)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = "Người dùng D7a" });
        return id;
    }

    private static async Task<Guid> BaiAsync(ModulesTestClient client, Guid author, string privacy = "public") =>
        (await client.CreatePostOkAsync(author, new { body = "Bài sẽ bị ẩn.", privacy, mediaKeys = Array.Empty<object>() }))
        .PostId;

    /// <summary>Ẩn bài qua hợp đồng C2, trong transaction của Moderation rồi COMMIT — hình dạng của D7c.</summary>
    private async Task AnAsync(Guid postId, string reasonCode = ReasonCodes.Spam)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var outcome = await scope.ServiceProvider.GetRequiredService<IModerationTargets>()
            .HideAsync(tx.GetDbTransaction(), new ModerationTarget(ModerationTargetType.Post, postId), reasonCode);
        Assert.Equal(HideOutcome.Hidden, outcome);
        await tx.CommitAsync();
    }

    private static async Task<JsonElement> DocJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Thân 404 rút gọn về ba trường người dò so được (bỏ <c>traceId</c>, <c>instance</c> — xem <c>ReadPostTests</c>).</summary>
    private static async Task<(string? Title, string? Detail, string? Type)> Than404Async(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await DocJsonAsync(response);
        return (root.GetProperty("title").GetString(), root.GetProperty("detail").GetString(),
            root.GetProperty("type").GetString());
    }

    /// <summary>
    /// <c>HID-01</c> — tác giả đọc bài <c>hidden</c>: 200 kèm <c>moderation { status, reasonCode, hiddenAt }</c>, <c>hiddenAt</c> đúng
    /// bằng <c>updated_at</c> của lần ẩn. Bài <c>published</c> của cùng tác giả: <c>moderation</c> là <c>null</c>.
    /// </summary>
    [Fact]
    public async Task HID_01_tac_gia_doc_bai_an_200_kem_moderation_bai_thuong_moderation_null()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var biAn = await BaiAsync(client, tacGia);
        var thuong = await BaiAsync(client, tacGia);
        await AnAsync(biAn, ReasonCodes.Harassment);

        using var response = await client.GetPostAsync(tacGia, biAn);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await DocJsonAsync(response);
        var moderation = root.GetProperty("moderation");
        Assert.Equal("hidden", moderation.GetProperty("status").GetString());
        Assert.Equal(ReasonCodes.Harassment, moderation.GetProperty("reasonCode").GetString());
        Assert.Equal(new[] { "hiddenAt", "reasonCode", "status" }, moderation.EnumerateObject().Select(p => p.Name).Order());

        var updatedAt = (DateTime)(await client.QueryRowAsync(
            "select updated_at from content.posts where post_id = $1", biAn))!["updated_at"]!;
        Assert.Equal(new DateTimeOffset(updatedAt, TimeSpan.Zero), moderation.GetProperty("hiddenAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("editedAt").ValueKind);   // hiddenAt không đến từ editedAt
        Assert.True(root.GetProperty("canEdit").GetBoolean());

        using var thuongResponse = await client.GetPostAsync(tacGia, thuong);
        Assert.Equal(HttpStatusCode.OK, thuongResponse.StatusCode);
        var thuongRoot = await DocJsonAsync(thuongResponse);
        Assert.True(!thuongRoot.TryGetProperty("moderation", out var m) || m.ValueKind == JsonValueKind.Null);
    }

    /// <summary>
    /// <c>HID-02</c> — bạn bè đọc bài <c>friends</c> đã bị ẩn: 404, CÙNG thân với id không tồn tại. Đọc trước khi ẩn là 200 — nên
    /// 404 sau đó đến từ nhánh BR-07, không phải từ BR-02.
    /// </summary>
    [Fact]
    public async Task HID_02_ban_be_doc_bai_an_404_cung_than_voi_khong_ton_tai()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var ban = await NguoiCoHoSoAsync(client);
        await client.MakeFriendsAsync(tacGia, ban);
        var bai = await BaiAsync(client, tacGia, "friends");

        using (var truoc = await client.GetPostAsync(ban, bai))
            Assert.Equal(HttpStatusCode.OK, truoc.StatusCode);

        await AnAsync(bai);

        using var sau = await client.GetPostAsync(ban, bai);
        using var khongTonTai = await client.GetPostAsync(ban, Guid.NewGuid());
        Assert.Equal(await Than404Async(khongTonTai), await Than404Async(sau));
    }

    /// <summary>
    /// <c>HID-03</c> — Moderator và Admin đọc bài công khai đã bị ẩn: 404 như mọi người khác. Nội dung cho người kiểm duyệt đi qua
    /// <c>GET /reports/{id}</c> (D7b), không qua đường vòng quanh BR-02. Người lạ cũng 404 (bài <c>public</c>: BR-02 cho qua, nên chỉ
    /// nhánh BR-07 chặn được).
    /// </summary>
    [Theory]
    [InlineData("MODERATOR")]
    [InlineData("ADMIN")]
    [InlineData("USER")]
    public async Task HID_03_moderator_admin_nguoi_la_doc_bai_cong_khai_bi_an_404(string role)
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        var nguoiDoc = Guid.NewGuid();

        using (var truoc = await client.GetPostAsync(nguoiDoc, bai, role))
            Assert.Equal(HttpStatusCode.OK, truoc.StatusCode);

        await AnAsync(bai);

        using var sau = await client.GetPostAsync(nguoiDoc, bai, role);
        using var khongTonTai = await client.GetPostAsync(nguoiDoc, Guid.NewGuid(), role);
        Assert.Equal(await Than404Async(khongTonTai), await Than404Async(sau));
    }

    /// <summary>
    /// <c>HID-04</c> — tác giả sửa bài bị ẩn: 409 <c>type …:post-hidden</c>, <c>body</c> và <c>editedAt</c> trong DB không đổi.
    /// Người khác sửa cùng bài vẫn 403 như GĐ2 (tầng 3 đứng TRƯỚC nhánh này) — 409 cho họ là lộ "bài này bị ẩn".
    /// </summary>
    [Fact]
    public async Task HID_04_tac_gia_sua_bai_an_409_post_hidden_khong_doi_gi_nguoi_khac_van_403()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        await AnAsync(bai);

        using var response = await client.UpdatePostAsync(tacGia, bai, new { body = "Sửa để tự gỡ ẩn.", privacy = "private" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await DocJsonAsync(response);
        Assert.Equal(ContentErrors.PostHiddenType, root.GetProperty("type").GetString());
        Assert.Equal("Bài viết đã bị ẩn do vi phạm tiêu chuẩn cộng đồng nên không sửa được.", root.GetProperty("detail").GetString());

        var row = await client.QueryRowAsync(
            "select body, privacy, status, edited_at from content.posts where post_id = $1", bai);
        Assert.Equal("Bài sẽ bị ẩn.", row!["body"]);
        Assert.Equal("public", row["privacy"]);
        Assert.Equal("hidden", row["status"]);
        Assert.Null(row["edited_at"]);

        using var nguoiKhac = await client.UpdatePostAsync(Guid.NewGuid(), bai, new { body = "Không phải bài của tôi." });
        Assert.Equal(HttpStatusCode.Forbidden, nguoiKhac.StatusCode);
    }

    /// <summary><c>HID-05</c> — tác giả xóa bài bị ẩn: 204, bài thành <c>deleted</c>; sau đó chính tác giả đọc cũng 404 (Đ-2.10).</summary>
    [Fact]
    public async Task HID_05_tac_gia_xoa_bai_an_204()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        await AnAsync(bai);

        using var response = await client.DeletePostAsync(tacGia, bai);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("deleted", (await client.QueryRowAsync(
            "select status from content.posts where post_id = $1", bai))!["status"]);

        using var doc = await client.GetPostAsync(tacGia, bai);
        Assert.Equal(HttpStatusCode.NotFound, doc.StatusCode);
    }

    /// <summary>
    /// <c>HID-06</c> — khẳng định lại Đ-4.11 qua API: bài bị ẩn không có trong feed lẫn trang cá nhân của chính tác giả, và trang
    /// cá nhân mà bạn bè xem. Bài thường cùng tác giả vẫn có — trang không rỗng vì lý do khác. Feed đọc LẦN ĐẦU sau khi ẩn: trang
    /// đầu cache 30s (Đ-4.8), đọc trước khi ẩn thì ca này đo cache, không đo bộ lọc.
    /// </summary>
    [Fact]
    public async Task HID_06_feed_va_trang_ca_nhan_khong_co_bai_an()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var ban = await NguoiCoHoSoAsync(client);
        await client.MakeFriendsAsync(tacGia, ban);
        var thuong = await BaiAsync(client, tacGia);
        var biAn = await BaiAsync(client, tacGia);
        await AnAsync(biAn);

        var feed = (await client.GetFeedOkAsync(tacGia)).Items.Select(p => p.PostId).ToList();
        var trangCuaMinh = (await client.ListPostsOkAsync(tacGia, tacGia)).Items.Select(p => p.PostId).ToList();
        var trangBanXem = (await client.ListPostsOkAsync(ban, tacGia)).Items.Select(p => p.PostId).ToList();

        foreach (var ids in new[] { feed, trangCuaMinh, trangBanXem })
        {
            Assert.Contains(thuong, ids);
            Assert.DoesNotContain(biAn, ids);
        }
    }
}
