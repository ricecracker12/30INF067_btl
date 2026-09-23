using System.Net;
using System.Net.Http.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D7 — <c>PATCH /posts/{postId}</c>. Khuôn tầng 3 của Mục 6.2: ba lý do trượt cho MỘT phản hồi 403, và không có nhánh
/// Admin nào.
///
/// Khác đường ĐỌC của D6 — ở đó ba lý do trượt cho 404 — và sự khác nhau đó là nội dung của quy ước 3b, không phải
/// mâu thuẫn: thao tác GHI cần ownership trả 403, thao tác ĐỌC nội dung có mức hiển thị trả 404.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UpdatePostTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Một tác giả đã onboarding kèm một bài <c>public</c> chỉ có chữ.</summary>
    private static async Task<(Guid Author, PostResponse Post)> ArrangeAsync(
        ModulesTestClient client, object? mediaKeys = null)
    {
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Chi Mai" });
        var post = await client.CreatePostOkAsync(
            author, new { body = "Nội dung gốc.", privacy = "public", mediaKeys = mediaKeys ?? Array.Empty<object>() });
        return (author, post);
    }

    /// <summary>
    /// <c>POST-06</c> — sửa <c>body</c> thành công: 200 với nội dung mới, <c>editedAt</c> khác <c>null</c>,
    /// <c>updated_at</c> ở DB ĐỔI còn <c>created_at</c> ĐỨNG YÊN, và <c>GET</c> lại thấy đúng nội dung mới.
    ///
    /// <c>updatedAt</c> đọc thẳng DB vì nó KHÔNG có trong <c>PostResponse</c> (hợp đồng không khai) — nhưng nó là thứ
    /// duy nhất chứng minh <c>ContentDbContext.SaveChangesAsync</c> đã đóng dấu, thay vì service gán tay.
    /// </summary>
    [Fact]
    public async Task POST_06_sua_body_tra_200_dong_dau_editedAt_va_updatedAt()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);
        var truoc = await client.QueryRowAsync(
            "SELECT created_at, updated_at FROM content.posts WHERE post_id = $1", post.PostId);

        var updated = await client.UpdatePostOkAsync(author, post.PostId, new { body = "Nội dung đã sửa." });

        Assert.Equal("Nội dung đã sửa.", updated.Body);
        Assert.NotNull(updated.EditedAt);
        Assert.Null(post.EditedAt);

        var sau = await client.QueryRowAsync(
            "SELECT created_at, updated_at, edited_at FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Equal(truoc!["created_at"], sau!["created_at"]);
        Assert.NotEqual(truoc["updated_at"], sau["updated_at"]);
        Assert.NotNull(sau["edited_at"]);

        using var doc = await client.GetPostAsync(author, post.PostId);
        var doclai = (await doc.Content.ReadFromJsonAsync<PostResponse>(ModulesTestClient.Json))!;
        Assert.Equal("Nội dung đã sửa.", doclai.Body);
    }

    /// <summary>
    /// Body <c>{}</c> → 400 <c>errors.body</c> "Không có gì để sửa.", KHÔNG phải 200 im lặng. Không chặn thì request
    /// rỗng vẫn đóng dấu <c>edited_at</c> và <c>updated_at</c> cho một thay đổi không tồn tại — bài hiện "đã chỉnh sửa"
    /// mà không có gì đổi.
    /// </summary>
    [Fact]
    public async Task Body_rong_tra_400_va_khong_dong_dau_gi()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using var response = await client.UpdatePostAsync(author, post.PostId, new { });
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal(ContentErrors.NothingToUpdateMessage, errors["body"].Single());

        var row = await client.QueryRowAsync("SELECT edited_at FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Null(row!["edited_at"]);
    }

    /// <summary>
    /// Gửi <c>mediaKeys</c> → 400 vì đó là <b>field lạ</b> (<c>UnmappedMemberHandling.Disallow</c>), không phải vì một
    /// luật validator nào. Đây là cơ chế duy nhất chặn "sửa ảnh ở GĐ2" (Mục 7.3), và nó chặn cả mảng rỗng.
    ///
    /// Key là <c>mediaKeys</c> nhờ <c>ValidationErrors</c> hạ <c>$.mediaKeys</c> — FE hiện lỗi dưới đúng ô.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"mediaKey\":\"x\",\"contentType\":\"image/jpeg\",\"sizeBytes\":1}]")]
    public async Task Gui_mediaKeys_tra_400_vi_la_field_la(string mediaKeysJson)
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/posts/{post.PostId:D}")
        {
            Content = new StringContent(
                $"{{\"body\":\"Sửa ảnh xem sao.\",\"mediaKeys\":{mediaKeysJson}}}",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(author);
        using var response = await client.Http.SendAsync(request);
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("mediaKeys", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>body: ""</c> trên bài CHỈ CÓ CHỮ → 400 <c>errors.body</c> với câu BR-01, vì bài sẽ không còn gì cả.
    /// <c>media_count</c> dùng ở đây là con số THẬT của bài đọc từ DB — không phải số ảnh trong request (request không
    /// có trường nào cho ảnh).
    /// </summary>
    [Fact]
    public async Task Bai_chi_chu_ma_xoa_het_chu_tra_400_theo_BR01()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using var response = await client.UpdatePostAsync(author, post.PostId, new { body = "" });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(PostContentPolicy.Empty, errors["body"].Single());
    }

    /// <summary>
    /// <c>body: ""</c> trên bài CÓ ẢNH → 200 và <c>body: null</c>. Cặp đôi với test trên: cùng một request, hai kết quả
    /// khác nhau, và thứ quyết định là <c>media_count</c> của bài — đúng ý "BR-01 áp lại với số ảnh thật".
    /// </summary>
    [Fact]
    public async Task Bai_co_anh_ma_xoa_het_chu_tra_200_voi_body_null()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Chi Mai" });
        var media = client.PutPostObject(author);
        var post = await client.CreatePostOkAsync(
            author, new { body = "Có chữ và ảnh.", privacy = "public", mediaKeys = new[] { media } });

        var updated = await client.UpdatePostOkAsync(author, post.PostId, new { body = "" });

        Assert.Null(updated.Body);
        Assert.Single(updated.Media);

        var row = await client.QueryRowAsync("SELECT body FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Null(row!["body"]);
    }

    /// <summary>
    /// Đổi <c>privacy</c> có hiệu lực ở lần ĐỌC kế tiếp, không phải lúc ghi (Mục 7.4). Người lạ đọc được bài
    /// <c>public</c>; tác giả chuyển sang <c>private</c>; người lạ đọc lại → 404.
    ///
    /// Khác test cùng ý ở <c>ReadPostTests</c>: ở đó tôi <c>UPDATE</c> thẳng DB vì D7 chưa có. Đây là bản đi qua API
    /// thật, tức là canh cả đường ghi lẫn đường đọc.
    /// </summary>
    [Fact]
    public async Task Doi_privacy_sang_private_thi_nguoi_la_het_doc_duoc()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);
        var nguoiLa = Guid.NewGuid();

        using (var truoc = await client.GetPostAsync(nguoiLa, post.PostId))
            Assert.Equal(HttpStatusCode.OK, truoc.StatusCode);

        var updated = await client.UpdatePostOkAsync(author, post.PostId, new { privacy = "private" });
        Assert.Equal(PostPrivacy.Private, updated.Privacy);
        Assert.Equal("Nội dung gốc.", updated.Body);   // chỉ gửi privacy thì body KHÔNG bị xóa

        using var sau = await client.GetPostAsync(nguoiLa, post.PostId);
        Assert.Equal(HttpStatusCode.NotFound, sau.StatusCode);
    }

    /// <summary>
    /// <b>Khuôn tầng 3 — ba lý do, một phản hồi 403.</b> Bài của người khác (<c>TC-A03</c>), bài không tồn tại, và bài
    /// đã xóa mềm. Cả ba phải KHÔNG phân biệt được từ ngoài: 404 cho ca "không tồn tại" là status code tự khai bài nào
    /// có thật.
    /// </summary>
    [Fact]
    public async Task Ba_ly_do_truot_deu_tra_403_khong_phan_biet_duoc()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);
        var (_, cuaNguoiKhac) = await ArrangeAsync(client);

        var daXoa = (await ArrangeAsync(client)).Post;
        await client.QueryRowAsync(
            "UPDATE content.posts SET status = 'deleted', deleted_at = now() WHERE post_id = $1 RETURNING post_id",
            daXoa.PostId);

        foreach (var id in new[] { cuaNguoiKhac.PostId, Guid.NewGuid(), daXoa.PostId })
        {
            using var response = await client.UpdatePostAsync(author, id, new { body = "Sửa trộm." });
            var (status, title, _) = await ModulesTestClient.ReadProblemAsync(response);

            Assert.Equal((int)HttpStatusCode.Forbidden, status);
            Assert.Equal(ProblemTitles.Forbidden, title);
        }

        // Bài của chính mình vẫn sửa được — nếu không thì test trên xanh vì lý do tầm thường (mọi PATCH đều 403).
        using var cuaMinh = await client.UpdatePostAsync(author, post.PostId, new { body = "Sửa bài của mình." });
        Assert.Equal(HttpStatusCode.OK, cuaMinh.StatusCode);
    }

    /// <summary>
    /// <b>Không có nhánh Admin ở tầng 3</b> (Mục 3.2 của GĐ1). ADMIN qua được tầng 2 nhờ short-circuit của
    /// <c>PermissionHandler</c>, nhưng vẫn KHÔNG sửa được bài của người khác — nếu qua được thì đó đúng là lỗ IDOR mà
    /// GOAL-03 muốn đóng.
    /// </summary>
    [Fact]
    public async Task ADMIN_qua_duoc_tang_2_nhung_van_khong_sua_duoc_bai_nguoi_khac()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangeAsync(client);

        using var response = await client.UpdatePostAsync(Guid.NewGuid(), post.PostId, new { body = "Admin sửa." }, role: "ADMIN");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary><c>body</c> quá 5000 ký tự → 400 <c>errors.body</c>, chặn ở validator trước khi chạm DB.</summary>
    [Fact]
    public async Task Body_qua_dai_tra_400()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangeAsync(client);

        using var response = await client.UpdatePostAsync(
            author, post.PostId, new { body = new string('x', PostContentPolicy.MaxBodyLength + 1) });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(PostContentPolicy.BodyTooLong, errors["body"].Single());
    }

    /// <summary><c>postId</c> sai dạng → 400 <c>errors.postId</c> (route không ràng buộc <c>:guid</c>, Mục 1.5).</summary>
    [Fact]
    public async Task postId_sai_dang_tra_400()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.UpdatePostAsync(Guid.NewGuid(), "khong-phai-guid", new { body = "x" });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("postId", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>Tầng 1 và tầng 2.</summary>
    [Fact]
    public async Task An_danh_401_va_vai_tro_thieu_quyen_403()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangeAsync(client);

        using var anDanh = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/posts/{post.PostId:D}")
        {
            Content = JsonContent.Create(new { body = "x" }),
        };
        using var khongToken = await client.Http.SendAsync(anDanh);
        Assert.Equal(HttpStatusCode.Unauthorized, khongToken.StatusCode);

        using var thieuQuyen = await client.UpdatePostAsync(Guid.NewGuid(), post.PostId, new { body = "x" }, role: "GUEST");
        Assert.Equal(HttpStatusCode.Forbidden, thieuQuyen.StatusCode);
    }
}
