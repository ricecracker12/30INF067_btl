using System.Net;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D8 — <c>DELETE /posts/{postId}</c>. Xóa MỀM (Đ-2.10): dòng <c>posts</c> còn, dòng <c>media_attachments</c> còn, object
/// trên R2 còn. Thứ duy nhất đổi là <c>status</c> và <c>deleted_at</c>.
///
/// Mọi khẳng định về DB ở lớp này đọc bằng <c>NpgsqlCommand</c> chứ KHÔNG qua <c>DbSet</c>: global query filter loại bài
/// <c>deleted</c> nên đọc qua EF sẽ thấy "không có dòng nào" và test tưởng bài đã bị xóa CỨNG — xanh vì lý do sai.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeletePostTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Tác giả đã onboarding kèm một bài <c>public</c> có một ảnh.</summary>
    private static async Task<(Guid Author, PostResponse Post)> ArrangeAsync(ModulesTestClient client)
    {
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Hoàng Yến" });
        var media = client.PutPostObject(author);
        var post = await client.CreatePostOkAsync(
            author, new { body = "Bài sắp xóa.", privacy = "public", mediaKeys = new[] { media } });
        return (author, post);
    }

    /// <summary>
    /// <c>POST-07</c> — xóa xong thì chính TÁC GIẢ <c>GET</c> cũng 404, và bài biến khỏi danh sách của chính họ. Bài đã
    /// xóa không còn là tài nguyên (Mục 7.3), không phải "tài nguyên ở trạng thái ẩn".
    /// </summary>
    [Fact]
    public async Task POST_07_xoa_xong_chinh_tac_gia_GET_cung_404_va_bai_bien_khoi_danh_sach()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using (var deleted = await client.DeletePostAsync(author, post.PostId))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var get = await client.GetPostAsync(author, post.PostId);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var page = await client.ListPostsOkAsync(author, author);
        Assert.DoesNotContain(post.PostId, page.Items.Select(p => p.PostId));
    }

    /// <summary>
    /// <b>Xóa MỀM, không xóa cứng.</b> Đọc bằng Npgsql để nhìn xuyên qua query filter: dòng <c>posts</c> vẫn còn với
    /// <c>status = 'deleted'</c> và <c>deleted_at</c> khác null, dòng <c>media_attachments</c> còn NGUYÊN, và
    /// <c>fake.Deleted</c> rỗng (không object R2 nào bị đụng).
    ///
    /// Ba khẳng định sau là ba lý do riêng biệt: dòng <c>posts</c> còn để khôi phục/kiểm duyệt được; dòng
    /// <c>media_attachments</c> còn vì <c>MediaCleanupWorker</c> (C4) tìm object mồ côi CHÍNH BẰNG chúng; object R2 còn
    /// vì xóa trong request là bấm nhầm một cái mất ảnh vĩnh viễn (Đ-2.10).
    /// </summary>
    [Fact]
    public async Task Xoa_mem_giu_nguyen_dong_posts_dong_media_va_object_R2()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using (var deleted = await client.DeletePostAsync(author, post.PostId))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var row = await client.QueryRowAsync(
            "SELECT status, deleted_at, media_count FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.NotNull(row);
        Assert.Equal("deleted", row["status"]);
        Assert.NotNull(row["deleted_at"]);
        Assert.Equal((short)1, row["media_count"]);

        var media = await client.QueryRowAsync(
            "SELECT count(*) AS n FROM content.media_attachments WHERE owner_id = $1", post.PostId);
        Assert.Equal(1L, media!["n"]);

        Assert.Empty(factory.Storage.Deleted);
    }

    /// <summary>
    /// <c>DELETE</c> lần hai → <b>403</b>, KHÔNG phải 404 hay 204. Bảng Mục 6.1 chốt thao tác ghi cần ownership dùng
    /// 403, và query filter làm điều đó xảy ra TỰ NHIÊN: lượt hai <c>FindForUpdateAsync</c> trả <c>null</c> và rơi vào
    /// đúng nhánh <c>Forbidden</c> sẵn có.
    ///
    /// Khác <c>DELETE /users/me/avatar</c> của D3 (idempotent, luôn 204) — và sự khác nhau đó là cố ý: ở đó không có
    /// tài nguyên nào có chủ để mà lộ, ở đây 204 lần hai là xác nhận "bài này từng tồn tại và là của bạn".
    /// </summary>
    [Fact]
    public async Task Xoa_lan_hai_tra_403_chu_khong_phai_404_hay_204()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using (var lanMot = await client.DeletePostAsync(author, post.PostId))
            Assert.Equal(HttpStatusCode.NoContent, lanMot.StatusCode);

        using var lanHai = await client.DeletePostAsync(author, post.PostId);
        var (status, title, _) = await ModulesTestClient.ReadProblemAsync(lanHai);

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Equal(ProblemTitles.Forbidden, title);
    }

    /// <summary>
    /// Bài đã xóa cũng không <c>PATCH</c> được nữa → 403. Hai endpoint ghi dùng chung một khuôn tầng 3, nên chúng phải
    /// trả lời giống nhau cho cùng một trạng thái.
    /// </summary>
    [Fact]
    public async Task Bai_da_xoa_thi_PATCH_cung_403()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);
        using (var _ = await client.DeletePostAsync(author, post.PostId)) { }

        using var response = await client.UpdatePostAsync(author, post.PostId, new { body = "Sửa bài đã xóa." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// <b>Khuôn tầng 3 — ba lý do, một phản hồi 403</b>, giống hệt <c>PATCH</c>: bài của người khác
    /// (<c>TC-A03-delete</c>), bài không tồn tại, bài đã xóa. Kèm ca đối chứng "bài của chính mình xóa được", nếu
    /// không thì một bản "mọi DELETE đều 403" vẫn làm test xanh.
    /// </summary>
    [Fact]
    public async Task Ba_ly_do_truot_deu_tra_403_va_bai_cua_minh_van_xoa_duoc()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);
        var (_, cuaNguoiKhac) = await ArrangeAsync(client);

        var daXoa = (await ArrangeAsync(client));
        using (var _ = await client.DeletePostAsync(daXoa.Author, daXoa.Post.PostId)) { }

        foreach (var id in new[] { cuaNguoiKhac.PostId, Guid.NewGuid(), daXoa.Post.PostId })
        {
            using var response = await client.DeletePostAsync(author, id);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var cuaMinh = await client.DeletePostAsync(author, post.PostId);
        Assert.Equal(HttpStatusCode.NoContent, cuaMinh.StatusCode);
    }

    /// <summary>
    /// <b>Không có nhánh Admin ở tầng 3</b> — ADMIN qua tầng 2 nhờ short-circuit của <c>PermissionHandler</c> nhưng vẫn
    /// không xóa được bài người khác. Cặp với test cùng tên ở <c>UpdatePostTests</c>: hai endpoint ghi, hai lần canh.
    /// </summary>
    [Fact]
    public async Task ADMIN_qua_duoc_tang_2_nhung_van_khong_xoa_duoc_bai_nguoi_khac()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangeAsync(client);

        using var response = await client.DeletePostAsync(Guid.NewGuid(), post.PostId, role: "ADMIN");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var row = await client.QueryRowAsync("SELECT status FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Equal("published", row!["status"]);
    }

    /// <summary>
    /// <b>Q-D7</b> — <c>postId</c> sai dạng → <b>400</b> <c>errors.postId</c>. Mã này ban đầu KHÔNG có trong hợp đồng
    /// (chỉ 204/401/403); đã thêm vào <c>content-v1.yaml</c> + <c>pnpm gen:api</c> trong chính commit này.
    ///
    /// Vì sao không ràng buộc route <c>:guid</c> để "không bao giờ ra 400": khi đó id hỏng cho <b>404</b>, lệch hẳn
    /// <c>GET</c>/<c>PATCH</c> cùng đường dẫn vốn đã hứa 400, và trùng mã với "không tìm thấy bài".
    /// </summary>
    [Fact]
    public async Task Q_D7_postId_sai_dang_tra_400_kem_errors_postId()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.DeletePostAsync(Guid.NewGuid(), "khong-phai-guid");
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("postId", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>Tầng 1 và tầng 2.</summary>
    [Fact]
    public async Task An_danh_401_va_vai_tro_thieu_quyen_403()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangeAsync(client);

        using var anDanh = await client.Http.DeleteAsync($"/api/v1/posts/{post.PostId:D}");
        Assert.Equal(HttpStatusCode.Unauthorized, anDanh.StatusCode);

        using var thieuQuyen = await client.DeletePostAsync(Guid.NewGuid(), post.PostId, role: "GUEST");
        Assert.Equal(HttpStatusCode.Forbidden, thieuQuyen.StatusCode);
    }
}
