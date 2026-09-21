using System.Net;
using System.Net.Http.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D6 — <c>GET /posts/{postId}</c>. BR-02 <b>tại thời điểm đọc</b> (Mục 7.4): mọi đường trượt đều là 404 với cùng một
/// body, vì trả 403 cho "không được xem" là để status code tự tố cáo bài có tồn tại.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReadPostTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Một tác giả đã onboarding kèm một bài ở mức <paramref name="privacy"/>.</summary>
    private static async Task<(Guid Author, PostResponse Post)> ArrangePostAsync(
        ModulesTestClient client, string privacy, object? mediaKeys = null)
    {
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Bình Minh" });
        var post = await client.CreatePostOkAsync(
            author, new { body = $"Bài {privacy}.", privacy, mediaKeys = mediaKeys ?? Array.Empty<object>() });
        return (author, post);
    }

    /// <summary>
    /// <c>READ-02..05</c> — ma trận BR-02 qua đường HTTP thật. Ở GĐ2 <c>AlwaysStrangers</c> luôn trả <c>false</c> nên
    /// <c>friends</c> hành xử giống <c>private</c> với người lạ: đó là hành vi ĐÃ CHỐT (Đ-2.9), không phải thiếu sót.
    ///
    /// Bảng đầy đủ mười hai tổ hợp ở <c>PostVisibilityTests</c> (unit); ở đây canh chính cái tầng unit không với tới —
    /// hàm thuần thật sự được nối vào đường đọc, và trượt BR-02 ra đúng <b>404</b> chứ không phải 403.
    /// </summary>
    [Theory]
    [InlineData("public", true, HttpStatusCode.OK)]
    [InlineData("public", false, HttpStatusCode.OK)]
    [InlineData("private", true, HttpStatusCode.OK)]
    [InlineData("private", false, HttpStatusCode.NotFound)]
    [InlineData("friends", true, HttpStatusCode.OK)]
    [InlineData("friends", false, HttpStatusCode.NotFound)]
    public async Task READ_02_05_ma_tran_BR02(string privacy, bool asAuthor, HttpStatusCode expected)
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangePostAsync(client, privacy);
        var reader = asAuthor ? author : Guid.NewGuid();

        using var response = await client.GetPostAsync(reader, post.PostId);

        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.NotFound)
            return;

        var (_, title, _) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(ProblemTitles.NotFound, title);
    }

    /// <summary>
    /// <b>Quy ước 3b</b> — "không tồn tại" và "không được xem" phải KHÔNG phân biệt được từ ngoài. So nguyên body hai
    /// phản hồi: lệch một chữ trong <c>detail</c> là lộ thông tin qua câu chữ dù status code giống hệt.
    ///
    /// Bỏ <c>traceId</c> và <c>instance</c> trước khi so, và cả hai đều CHÍNH ĐÁNG: <c>traceId</c> mới mỗi request theo
    /// thiết kế, còn <c>instance</c> là chính URL người gọi vừa gõ — nó không nói cho họ điều gì họ chưa biết. Mọi
    /// trường còn lại (<c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>) phải giống hệt.
    /// </summary>
    [Fact]
    public async Task Bai_khong_ton_tai_va_bai_khong_duoc_xem_tra_phan_hoi_giong_het()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangePostAsync(client, "private");
        var stranger = Guid.NewGuid();

        using var khongDuocXem = await client.GetPostAsync(stranger, post.PostId);
        using var khongTonTai = await client.GetPostAsync(stranger, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, khongDuocXem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, khongTonTai.StatusCode);

        static string Normalize(string body) =>
            System.Text.RegularExpressions.Regex.Replace(
                body,
                "\"(traceId|instance)\":\"[^\"]*\"",
                "",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1));

        Assert.Equal(
            Normalize(await khongTonTai.Content.ReadAsStringAsync()),
            Normalize(await khongDuocXem.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// <b>"Tại thời điểm đọc"</b> — đổi <c>privacy</c> có hiệu lực ở request KẾ TIẾP, không có bản sao quyền xem nào bị
    /// đóng băng lúc ghi. Ở GĐ2 chưa có <c>PATCH</c> (D7) nên kiểm bằng cách khác: cùng một bài <c>public</c>, người lạ
    /// đọc được; <c>UPDATE</c> thẳng cột <c>privacy</c> trong DB rồi đọc lại → 404. Nếu quyền xem được tính lúc tạo bài
    /// và lưu đâu đó, lượt đọc thứ hai vẫn 200.
    /// </summary>
    [Fact]
    public async Task Doi_privacy_co_hieu_luc_ngay_o_lan_doc_ke_tiep()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangePostAsync(client, "public");
        var stranger = Guid.NewGuid();

        using (var truoc = await client.GetPostAsync(stranger, post.PostId))
            Assert.Equal(HttpStatusCode.OK, truoc.StatusCode);

        await client.QueryRowAsync(
            "UPDATE content.posts SET privacy = 'private' WHERE post_id = $1 RETURNING post_id", post.PostId);

        using var sau = await client.GetPostAsync(stranger, post.PostId);
        Assert.Equal(HttpStatusCode.NotFound, sau.StatusCode);
    }

    /// <summary>
    /// Bài đã xóa mềm biến mất khỏi đường đọc, kể cả với CHÍNH tác giả (Đ-2.10, Mục 7.3). D8 chưa có nên xóa thẳng
    /// trong DB — điều cần canh ở đây là global query filter thật sự áp cho <c>FindAsync</c>, chứ không phải endpoint
    /// <c>DELETE</c>.
    ///
    /// Đây cũng là lưới cho luật 7 của khối D: thêm <c>IgnoreQueryFilters()</c> vào đường đọc là test này đỏ.
    /// </summary>
    [Fact]
    public async Task Bai_da_xoa_mem_thi_chinh_tac_gia_cung_404()
    {
        var client = new ModulesTestClient(factory);
        var (author, post) = await ArrangePostAsync(client, "public");

        await client.QueryRowAsync(
            "UPDATE content.posts SET status = 'deleted', deleted_at = now() WHERE post_id = $1 RETURNING post_id",
            post.PostId);

        using var response = await client.GetPostAsync(author, post.PostId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Nhánh 200 trả ĐÚNG hình dạng <c>PostResponse</c> mà <c>POST /posts</c> đã trả, kể cả URL ảnh đã ký và
    /// <c>canEdit</c> tính theo NGƯỜI ĐỌC (không phải theo bài). Đây là khẳng định "một hình dạng cho mọi endpoint trả
    /// bài" — hai chỗ ánh xạ là hai chỗ lệch nhau.
    /// </summary>
    [Fact]
    public async Task Doc_bai_cong_khai_tra_dung_hinh_dang_va_canEdit_theo_nguoi_doc()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Bình Minh" });
        var media = client.PutPostObject(author);
        var created = await client.CreatePostOkAsync(
            author, new { body = "Có ảnh.", privacy = "public", mediaKeys = new[] { media } });

        using var boiTacGia = await client.GetPostAsync(author, created.PostId);
        var mine = (await boiTacGia.Content.ReadFromJsonAsync<PostResponse>(ModulesTestClient.Json))!;

        using var boiNguoiLa = await client.GetPostAsync(Guid.NewGuid(), created.PostId);
        var theirs = (await boiNguoiLa.Content.ReadFromJsonAsync<PostResponse>(ModulesTestClient.Json))!;

        Assert.Equal(created.PostId, mine.PostId);
        Assert.Equal("Có ảnh.", mine.Body);
        Assert.Equal(PostPrivacy.Public, mine.Privacy);
        Assert.Equal("Bình Minh", mine.Author.DisplayName);
        Assert.StartsWith("https://fake.invalid/get/posts/", Assert.Single(mine.Media).Url, StringComparison.Ordinal);
        Assert.Empty(mine.ReactionCounts);

        Assert.True(mine.CanEdit);
        Assert.False(theirs.CanEdit);
        Assert.Equal(mine.PostId, theirs.PostId);
    }

    /// <summary>
    /// <c>postId</c> sai dạng → <b>400</b> <c>errors.postId</c>, KHÔNG phải 404. Route cố ý không có ràng buộc
    /// <c>:guid</c> (Mục 1.5): có ràng buộc thì id hỏng ra 404, trùng mã với "không tìm thấy bài" và FE đọc nhầm hai
    /// chuyện thành một.
    /// </summary>
    [Fact]
    public async Task postId_sai_dang_tra_400_kem_errors_postId()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.GetPostAsync(Guid.NewGuid(), "khong-phai-guid");
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("postId", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>Tầng 1 và tầng 2: ẩn danh → 401; vai trò thiếu <c>post.read.public</c> → 403.</summary>
    [Fact]
    public async Task An_danh_401_va_vai_tro_thieu_quyen_403()
    {
        var client = new ModulesTestClient(factory);
        var (_, post) = await ArrangePostAsync(client, "public");

        using var anDanh = await client.Http.GetAsync($"/api/v1/posts/{post.PostId:D}");
        Assert.Equal(HttpStatusCode.Unauthorized, anDanh.StatusCode);

        using var khach = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/posts/{post.PostId:D}");
        khach.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), "GUEST");
        using var thieuQuyen = await client.Http.SendAsync(khach);
        Assert.Equal(HttpStatusCode.Forbidden, thieuQuyen.StatusCode);
    }
}
