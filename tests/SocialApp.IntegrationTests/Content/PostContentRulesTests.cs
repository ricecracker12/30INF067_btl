using System.Net;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// <b>B3 Bước 3</b> — bốn test BR-01 đi qua <c>POST /posts</c> thật (<c>AC-02</c>, <c>AC-03</c>, <c>BR01-05</c>,
/// <c>BR01-06</c>), gom về khối B theo chốt <b>Q-B3</b>.
///
/// Vì sao chúng ở đây chứ không ở <c>CreatePostTests</c> (D5): cả bốn là khẳng định <b>"BR-01 vẫn còn nguyên"</b>, không
/// phải "endpoint chạy được". Lớp của D5 chết thì D5 hỏng; lớp này chết thì một luật nghiệp vụ đã mất mà endpoint vẫn
/// chạy ngon — hai loại hỏng khác nhau, và loại thứ hai là loại không ai nhận ra.
///
/// <c>BR01-05</c> còn canh một thứ mà D5 <b>không tự canh được</b>: <c>HEAD</c> phải đứng <b>TRƯỚC</b> transaction
/// (B.9, chỗ thứ 2 trong năm thứ chỉ code review bắt được). Đảo thứ tự thì bài vẫn bị từ chối bằng đúng mã 400 — chỉ
/// khác là đã có một dòng <c>posts</c> ra đời rồi phải rollback. Khẳng định "số dòng không tăng" là thứ duy nhất nhìn
/// thấy khác biệt đó.
///
/// Lớp này <b>sửa</b> dữ liệu nên dùng database riêng (<c>UseFreshDatabaseAsync</c> → <c>CreateDatabaseAsync</c>), không
/// dùng bản seed dùng chung — luật chọn hàm của <c>B1</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostContentRulesTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> OnboardedAuthorAsync(ModulesTestClient client)
    {
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Tác giả BR-01" });
        return author;
    }

    private async Task<long> PostCountAsync(ModulesTestClient client, Guid author) =>
        (long)(await client.QueryRowAsync("SELECT count(*) AS n FROM content.posts WHERE author_id = $1", author))!["n"]!;

    /// <summary>
    /// <c>AC-02</c> — bài không chữ, không ảnh → 400 và <c>errors</c> có key <b><c>body</c></b>.
    ///
    /// Key là phần bắt buộc, không phải mã lỗi: luật frontend Mục 6 bắt FE hiện lỗi theo key, nên một 400 đúng mã mà
    /// sai key là lỗi hiện dưới không ô nào. <c>body</c> chứ không <c>mediaKeys</c> vì người dùng đang đứng ở ô soạn
    /// chữ và đường thoát rẻ nhất là gõ một chữ (lập luận của <c>PostContentPolicy</c>).
    ///
    /// Bốn cách "rỗng" phải cho cùng một kết quả: vắng mặt, <c>null</c>, <c>""</c>, và toàn khoảng trắng.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  ")]
    public async Task AC_02_bai_khong_chu_khong_anh_tra_400_duoi_key_body(string? body)
    {
        var client = new ModulesTestClient(factory);
        var author = await OnboardedAuthorAsync(client);

        using var response = await client.CreatePostAsync(
            author, new { body, privacy = "public", mediaKeys = Array.Empty<object>() });
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal([PostContentPolicy.BodyKey], errors.Keys);
        Assert.Equal(PostContentPolicy.Empty, errors[PostContentPolicy.BodyKey].Single());
        Assert.Equal(0L, await PostCountAsync(client, author));
    }

    /// <summary>
    /// <c>AC-03</c> — 11 ảnh → 400 và <c>errors</c> có key <b><c>mediaKeys</c></b>.
    ///
    /// Gửi KÈM <c>body</c> hợp lệ: nếu không, một bản cài đặt kiểm <c>body</c> trước sẽ trả <c>errors.body</c> và test
    /// vẫn "thấy 400" mà sai hẳn trường. Mười ảnh là biên TRÊN được nhận — có ca đối chứng bên dưới.
    /// </summary>
    [Fact]
    public async Task AC_03_muoi_mot_anh_tra_400_duoi_key_mediaKeys()
    {
        var client = new ModulesTestClient(factory);
        var author = await OnboardedAuthorAsync(client);
        var media = Enumerable.Range(0, PostContentPolicy.MaxMediaCount + 1)
            .Select(_ => client.PutPostObject(author))
            .ToArray();

        using var response = await client.CreatePostAsync(
            author, new { body = "Có chữ đàng hoàng.", privacy = "public", mediaKeys = media });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal([PostContentPolicy.MediaKeysKey], errors.Keys);
        Assert.Equal(PostContentPolicy.TooManyMedia, errors[PostContentPolicy.MediaKeysKey].Single());
        Assert.Equal(0L, await PostCountAsync(client, author));
    }

    /// <summary>
    /// Đối chứng của <c>AC-03</c>: đúng 10 ảnh → <b>201</b>. Thiếu ca này thì một bản cài đặt chặn từ ảnh thứ nhất cũng
    /// làm <c>AC-03</c> xanh, và không ai đăng được bài nhiều ảnh.
    /// </summary>
    [Fact]
    public async Task Doi_chung_AC_03_dung_muoi_anh_van_tao_duoc()
    {
        var client = new ModulesTestClient(factory);
        var author = await OnboardedAuthorAsync(client);
        var media = Enumerable.Range(0, PostContentPolicy.MaxMediaCount)
            .Select(_ => client.PutPostObject(author))
            .ToArray();

        var post = await client.CreatePostOkAsync(
            author, new { body = "Đủ mười ảnh.", privacy = "public", mediaKeys = media });

        Assert.Equal(PostContentPolicy.MaxMediaCount, post.Media.Count);
    }

    /// <summary>
    /// <c>BR01-05</c> — client khai <b>1 MB</b>, object thật trong bucket là <b>12 MB</b> → 400, và
    /// <b>số dòng <c>content.posts</c> KHÔNG tăng</b>.
    ///
    /// Khẳng định thứ hai quan trọng hơn mã lỗi, và là lý do mã này tồn tại: nó canh <c>HEAD</c> đứng TRƯỚC transaction.
    /// Đảo thứ tự (HEAD sau khi đã <c>Add</c> vào tracker, hay trong cùng một <c>using var tx</c>) thì người dùng vẫn
    /// nhận đúng 400 — chỉ khác là đã có một dòng ra đời rồi phải rollback, và không test nào khác nhìn thấy.
    ///
    /// 12 MB vượt cả <c>MaxSizeBytes</c>, nên đây cũng là ca chứng minh lớp 1 (khai báo) KHÔNG thay được lớp 2 (sự
    /// thật trong bucket): khai 1 MB thì validator cho qua, chỉ <c>HEAD</c> mới nói được object thật to bao nhiêu.
    /// Object dựng bằng <c>FakeObjectStorage</c>, không gọi R2 thật (Mục 10.2 mức 2).
    /// </summary>
    [Fact]
    public async Task BR01_05_khai_1MB_object_that_12MB_thi_400_va_khong_tao_dong_nao()
    {
        var client = new ModulesTestClient(factory);
        var author = await OnboardedAuthorAsync(client);

        var key = StorageKeys.ForPost(author, "image/jpeg");
        client.PutObject(key, 12L * 1024 * 1024, "image/jpeg");
        var truoc = await PostCountAsync(client, author);

        using var response = await client.CreatePostAsync(
            author,
            new
            {
                body = "Ảnh to hơn khai báo.",
                privacy = "public",
                mediaKeys = new[] { new { mediaKey = key, contentType = "image/jpeg", sizeBytes = 1L * 1024 * 1024 } },
            });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(MediaHeadPolicy.SizeMismatch, errors[PostContentPolicy.MediaKeysKey].Single());

        Assert.Equal(truoc, await PostCountAsync(client, author));
        Assert.Equal(0L, (long)(await client.QueryRowAsync(
            "SELECT count(*) AS n FROM content.media_attachments WHERE storage_key = $1", key))!["n"]!);
    }

    /// <summary>
    /// <c>BR01-06</c> — bài CHỈ có ảnh, không có chữ → <b>201</b>.
    ///
    /// Canh hai thứ cùng lúc: <c>ck_posts_not_empty</c> không chặn nhầm (mệnh đề 3 của BR-01 là "có ảnh HOẶC có chữ",
    /// không phải "và"), và thứ tự INSERT đúng — <c>media_count</c> phải mang con số thật NGAY Ở CÂU INSERT, vì INSERT
    /// 0 rồi UPDATE sau thì CHECK nổ ngay tại câu đầu với đúng loại bài này.
    ///
    /// Đọc lại DB bằng Npgsql để khẳng định <c>body</c> là <c>NULL</c> chứ không phải chuỗi rỗng: CHECK so
    /// <c>btrim(coalesce(body,''))</c> nên cả hai đều lọt, nhưng hợp đồng ghi <c>body: null</c> và FE đọc theo đó.
    /// </summary>
    [Fact]
    public async Task BR01_06_bai_chi_co_anh_khong_co_chu_tra_201()
    {
        var client = new ModulesTestClient(factory);
        var author = await OnboardedAuthorAsync(client);
        var media = new[] { client.PutPostObject(author), client.PutPostObject(author, "image/png") };

        var post = await client.CreatePostOkAsync(author, new { privacy = "public", mediaKeys = media });

        Assert.Null(post.Body);
        Assert.Equal(2, post.Media.Count);

        var row = await client.QueryRowAsync(
            "SELECT body, media_count, status FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Null(row!["body"]);
        Assert.Equal((short)2, row["media_count"]);
        Assert.Equal("published", row["status"]);
    }
}
