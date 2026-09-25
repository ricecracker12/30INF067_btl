using System.Net;
using System.Net.Http.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D5 — <c>POST /posts</c>. Sáu bước kiểm của hợp đồng có test riêng cho từng bước, vì chúng trả bốn mã khác nhau
/// (403/400/409/201) và lẫn thứ tự thì vẫn "có lỗi" mà sai loại lỗi — đúng thứ người dùng không sửa nổi.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreatePostTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private const string DisplayName = "An Nguyễn";

    /// <summary>Hồ sơ có thật cho <paramref name="actor"/> — Đ-2.4 bắt buộc, trừ ca cố ý bỏ qua.</summary>
    private static Task OnboardAsync(ModulesTestClient client, Guid actor) =>
        client.PutProfileOkAsync(actor, new { displayName = DisplayName });

    /// <summary>
    /// <c>AC-01</c> — nhánh chính. Kiểm đủ thứ hợp đồng hứa cho một bài vừa tạo, không chỉ mã 201: một dòng DB với
    /// <c>media_count</c> đúng <b>ngay từ INSERT</b>, URL ảnh đã ký, <c>canEdit</c> do server tính, tác giả lấy qua
    /// <c>IUserDirectory</c>, và hai trường khung của Đ-2.12 (<c>commentCount</c> 0, <c>reactionCounts</c> <b>{}</b> chứ
    /// không null).
    /// </summary>
    [Fact]
    public async Task AC_01_dang_bai_kem_mot_anh_tra_201_dung_hinh_dang_PostResponse()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var media = client.PutPostObject(actor);

        var post = await client.CreatePostOkAsync(
            actor, new { body = "Chiều nay ở phố cổ.", privacy = "public", mediaKeys = new[] { media } });

        Assert.Equal("Chiều nay ở phố cổ.", post.Body);
        Assert.Equal(PostPrivacy.Public, post.Privacy);
        Assert.True(post.CanEdit);
        Assert.Equal(actor, post.Author.UserId);
        Assert.Equal(DisplayName, post.Author.DisplayName);
        Assert.Equal(0, post.CommentCount);
        Assert.Empty(post.ReactionCounts);
        Assert.Null(post.EditedAt);

        var image = Assert.Single(post.Media);
        Assert.Equal(0, image.Position);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.StartsWith("https://fake.invalid/get/posts/", image.Url, StringComparison.Ordinal);

        var row = await client.QueryRowAsync(
            "SELECT media_count, status, body FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Equal((short)1, row!["media_count"]);
        Assert.Equal("published", row["status"]);
        Assert.Equal("Chiều nay ở phố cổ.", row["body"]);
    }

    /// <summary>
    /// GĐ7 C2: <c>socialapp_posts_created_total</c> tăng đúng 1 khi bài được lưu, và KHÔNG tăng khi bị từ chối — đếm trước
    /// lúc lưu thì biểu đồ "bài mới" vẫn lên đều trong khi không ai đăng được bài nào.
    /// </summary>
    [Fact]
    public async Task C2_posts_created_tang_dung_1_khi_luu_khong_tang_khi_bi_tu_choi()
    {
        const string Metric = "socialapp_posts_created_total";
        var client = new ModulesTestClient(factory);
        using var http = factory.CreateClient();
        var actor = Guid.NewGuid();
        var truoc = await MetricsReader.ReadAsync(http, Metric);

        // Chưa có hồ sơ → 403 (Đ-2.4): bị từ chối ở bước (2), không được đếm.
        using (var biTuChoi = await client.CreatePostAsync(actor, new { body = "Chưa có hồ sơ", privacy = "public" }))
            Assert.Equal(HttpStatusCode.Forbidden, biTuChoi.StatusCode);
        Assert.Equal(truoc, await MetricsReader.ReadAsync(http, Metric));

        await OnboardAsync(client, actor);
        await client.CreatePostOkAsync(actor, new { body = "Bài đầu tiên", privacy = "public" });
        Assert.Equal(truoc + 1, await MetricsReader.ReadAsync(http, Metric));
    }

    /// <summary>
    /// <c>reactionCounts</c> ra JSON là <c>{}</c>, không phải <c>null</c> và không vắng mặt (Đ-2.12, Mục 8.2). Khẳng
    /// định trên JSON THÔ: DTO đọc lại được cả ba dạng nên test qua DTO sẽ xanh với bản sai. FE của E5 viết
    /// <c>Object.entries(reactionCounts)</c> một lần và không phân nhánh — đổi sang null là vỡ ở GĐ3.
    /// </summary>
    [Fact]
    public async Task reactionCounts_ra_JSON_la_object_rong_khong_phai_null()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        using var response = await client.CreatePostAsync(
            actor, new { body = "Chỉ chữ.", privacy = "private", mediaKeys = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"reactionCounts\":{}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reactionCounts\":null", json, StringComparison.Ordinal);
        // `mediaKey` là chi tiết nội bộ — hợp đồng cố ý không có nó trong PostMedia.
        Assert.DoesNotContain("mediaKey", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bài CHỈ có ảnh, <c>body</c> vắng mặt → 201 và <c>body: null</c>. <c>ck_posts_not_empty</c> không chặn nhầm, và
    /// <c>media_count</c> được gán ĐÚNG ngay ở câu INSERT — gán 0 rồi UPDATE sau thì CHECK nổ ngay tại INSERT.
    ///
    /// Bản chính thức của <c>BR01-06</c> nay ở <c>PostContentRulesTests</c> (B3). Giữ ca này vì nó khẳng định thêm một
    /// thứ bản kia không có: <c>position</c> trong PHẢN HỒI của một bài hai ảnh.
    /// </summary>
    [Fact]
    public async Task Bai_chi_co_anh_khong_co_chu_van_tao_duoc_va_body_la_null()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var media = new[] { client.PutPostObject(actor), client.PutPostObject(actor, "image/png") };

        var post = await client.CreatePostOkAsync(actor, new { privacy = "public", mediaKeys = media });

        Assert.Null(post.Body);
        Assert.Equal(2, post.Media.Count);
        Assert.Equal([0, 1], post.Media.Select(m => m.Position));

        var row = await client.QueryRowAsync("SELECT media_count, body FROM content.posts WHERE post_id = $1", post.PostId);
        Assert.Equal((short)2, row!["media_count"]);
        Assert.Null(row["body"]);
    }

    /// <summary>
    /// Thứ tự trong <c>mediaKeys</c> là <c>position</c> hiển thị (0..9) — hợp đồng ghi rõ. Ba ảnh khác loại để phân biệt
    /// được từng cái: nếu mapper sắp theo thứ tự store trả về thay vì theo <c>Position</c>, hoặc service gán
    /// <c>position</c> theo thứ tự khác, test này đỏ.
    /// </summary>
    [Fact]
    public async Task Thu_tu_mediaKeys_tro_thanh_position_hien_thi()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var media = new[]
        {
            client.PutPostObject(actor, "image/webp"),
            client.PutPostObject(actor, "image/jpeg"),
            client.PutPostObject(actor, "image/png"),
        };

        var post = await client.CreatePostOkAsync(actor, new { body = "Ba ảnh.", privacy = "public", mediaKeys = media });

        Assert.Equal(["image/webp", "image/jpeg", "image/png"], post.Media.Select(m => m.ContentType));
        Assert.Equal([0, 1, 2], post.Media.Select(m => m.Position));
    }

    /// <summary>
    /// <b>Đ-2.4</b> — chưa onboarding thì không đăng được bài. MỘT test hai bước, cố ý: bước 2 chứng minh 403 ở bước 1
    /// đúng là do thiếu hồ sơ chứ không do thứ gì khác trong request (mọi thứ còn lại giữ nguyên).
    ///
    /// Đây cũng là nền của <c>Q-B2</c>: dòng matrix <c>TC-A03-media</c> phải tạo hồ sơ cho người gọi trước, nếu không nó
    /// xanh vì chính nhánh này chứ không vì kiểm tiền tố khóa.
    /// </summary>
    [Fact]
    public async Task Chua_co_ho_so_thi_403_co_ho_so_roi_thi_201_cung_mot_request()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var body = new { body = "Bài đầu tiên.", privacy = "public", mediaKeys = Array.Empty<object>() };

        using (var truoc = await client.CreatePostAsync(actor, body))
        {
            var (status, title, _) = await ModulesTestClient.ReadProblemAsync(truoc);
            Assert.Equal((int)HttpStatusCode.Forbidden, status);
            Assert.Equal(ProblemTitles.Forbidden, title);
            // Sửa 2026-09-25: 403 chưa có hồ sơ mang `type` riêng — FE tách nó khỏi 403 thiếu `post.create` (content-v1 1.3.0-gd6).
            Assert.Equal(ContentErrors.ProfileRequiredType, await ProblemTypeAsync(truoc));
        }

        await OnboardAsync(client, actor);

        using var sau = await client.CreatePostAsync(actor, body);
        Assert.Equal(HttpStatusCode.Created, sau.StatusCode);
    }

    /// <summary>
    /// <b>Đ-2.7 / <c>TC-A03-media</c></b> — ảnh dưới tiền tố của NGƯỜI KHÁC → 403, và <c>HeadCalls</c> KHÔNG tăng.
    ///
    /// Bộ đếm là phần quan trọng ngang mã lỗi: kiểm tiền tố phải đứng TRƯỚC HEAD, nếu không thì mỗi key kẻ dò gửi lên là
    /// một lời gọi R2 có tính tiền — và chỉ khẳng định 403 thì đảo thứ tự vẫn xanh (mã trả về giống hệt, chỉ khác một
    /// hóa đơn). Cùng lưới với <c>PROF_04</c> của D3.
    ///
    /// Cũng khẳng định phản hồi không chứa key: nó là dữ liệu của người khác.
    /// </summary>
    [Fact]
    public async Task TC_A03_media_anh_cua_nguoi_khac_tra_403_va_khong_ton_mot_luot_HEAD()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var victimMedia = client.PutPostObject(Guid.NewGuid());
        var headsBefore = factory.Storage.HeadCalls;

        using var response = await client.CreatePostAsync(
            actor, new { body = "Ảnh này không phải của tôi.", privacy = "public", mediaKeys = new[] { victimMedia } });
        var (status, title, _) = await ModulesTestClient.ReadProblemAsync(response);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Equal(ProblemTitles.Forbidden, title);
        Assert.Equal(headsBefore, factory.Storage.HeadCalls);
        Assert.DoesNotContain("posts/", responseBody, StringComparison.Ordinal);
        // Key của người khác KHÔNG có `type` riêng — cùng phản hồi với 403 thiếu quyền (Đ-2.7): `profile-required` chỉ tách nhánh
        // chưa có hồ sơ, không mở máy dò key.
        Assert.Equal("https://httpstatuses.io/403", await ProblemTypeAsync(response));
    }

    /// <summary><c>type</c> của Problem Details (thân đã đệm — đọc lại được sau <c>ReadProblemAsync</c>).</summary>
    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("type").GetString();
    }

    /// <summary>
    /// <b>Đ-2.8 lớp 2</b> — HEAD gọi ĐÚNG một lần cho mỗi key, và bài KHÔNG được tạo khi một object chưa có trên bucket.
    /// Ảnh thứ hai cố ý không <c>Put</c>: vòng lặp phải HEAD tới nó, không được tin khai báo của client.
    ///
    /// Đếm chênh lệch đúng bằng số key đã kiểm (2) chứ không phải 0 hay 1: 0 nghĩa là bỏ hẳn lớp 2, 1 nghĩa là chỉ kiểm
    /// key đầu rồi tin phần còn lại.
    /// </summary>
    [Fact]
    public async Task Object_chua_tai_len_thi_400_va_HEAD_da_chay_du_so_key()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        var uploaded = client.PutPostObject(actor);
        // Key đúng dạng, đúng tiền tố của mình, nhưng KHÔNG gọi PutObject — chưa có gì trong bucket.
        var missing = new { mediaKey = StorageKeys.ForPost(actor, "image/png"), contentType = "image/png", sizeBytes = 2048L };
        var headsBefore = factory.Storage.HeadCalls;

        using var response = await client.CreatePostAsync(
            actor, new { body = "Một ảnh chưa xong.", privacy = "public", mediaKeys = new object[] { uploaded, missing } });
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal(MediaHeadPolicy.NotUploaded, errors["mediaKeys"].Single());
        Assert.Equal(headsBefore + 2, factory.Storage.HeadCalls);

        var row = await client.QueryRowAsync("SELECT count(*) AS n FROM content.posts WHERE author_id = $1", actor);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>
    /// Đ-2.8 lớp 2, hai nhánh còn lại: object CÓ thật nhưng lệch dung lượng hoặc lệch loại so với khai báo lúc presign.
    /// Chữ ký nói "client ĐỊNH PUT cái gì", HEAD nói "trong bucket ĐANG CÓ cái gì" — chỉ HEAD nói được sự thật.
    /// </summary>
    [Theory]
    [InlineData(2048L, "image/jpeg", "Dung lượng ảnh không khớp với khai báo lúc xin tải lên.")]
    [InlineData(1024L, "image/png", "Loại ảnh không khớp với khai báo lúc xin tải lên.")]
    public async Task HEAD_lech_khai_bao_thi_400_kem_dung_cau(long declaredSize, string declaredType, string expected)
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        // Trong bucket: 1024 byte, image/jpeg. Khai báo gửi lên cố ý lệch một trong hai.
        var key = StorageKeys.ForPost(actor, "image/jpeg");
        client.PutObject(key, 1024, "image/jpeg");

        using var response = await client.CreatePostAsync(
            actor,
            new
            {
                body = "Khai một đằng.",
                privacy = "public",
                mediaKeys = new[] { new { mediaKey = key, contentType = declaredType, sizeBytes = declaredSize } },
            });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(expected, errors["mediaKeys"].Single());
    }

    /// <summary>
    /// <c>POST-08</c> — commit lại CÙNG một <c>mediaKey</c> ở bài thứ hai → <b>409</b>, và số dòng <c>posts</c> KHÔNG
    /// tăng. UNIQUE <c>storage_key</c> là thứ chặn "gắn một object vào hai bài"; để exception rơi thành 500 thì người
    /// dùng không biết chuyện gì xảy ra, và log đầy stack trace cho một luồng nghiệp vụ bình thường.
    /// </summary>
    [Fact]
    public async Task POST_08_dung_lai_anh_da_gan_bai_khac_tra_409_va_khong_tao_them_bai()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var media = client.PutPostObject(actor);

        await client.CreatePostOkAsync(actor, new { body = "Bài một.", privacy = "public", mediaKeys = new[] { media } });

        using var second = await client.CreatePostAsync(
            actor, new { body = "Bài hai, cùng ảnh.", privacy = "public", mediaKeys = new[] { media } });
        var (status, title, _) = await ModulesTestClient.ReadProblemAsync(second);

        Assert.Equal((int)HttpStatusCode.Conflict, status);
        Assert.Equal(ProblemTitles.Conflict, title);

        var row = await client.QueryRowAsync("SELECT count(*) AS n FROM content.posts WHERE author_id = $1", actor);
        Assert.Equal(1L, row!["n"]);
    }

    /// <summary>
    /// Hai key TRÙNG NHAU trong CÙNG một request → 400 <c>errors.mediaKeys</c>, không phải 409. Để nó chạm DB thì UNIQUE
    /// nổ và người dùng nhận "ảnh đã dùng ở bài khác" — sai hẳn nguyên nhân, vì "bài khác" đó chính là bài đang gửi.
    /// </summary>
    [Fact]
    public async Task Hai_key_trung_nhau_trong_mot_request_tra_400_chu_khong_phai_409()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var media = client.PutPostObject(actor);

        using var response = await client.CreatePostAsync(
            actor, new { body = "Cùng một ảnh hai lần.", privacy = "public", mediaKeys = new[] { media, media } });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ContentErrors.DuplicateMediaKeysMessage, errors["mediaKeys"].Single());
    }

    /// <summary>
    /// <b>Q-D2</b> — <c>privacy</c> vắng mặt → 400 <c>errors.privacy</c>, KHÔNG âm thầm thành <c>public</c>. Đây là
    /// trường mà quyết định đó sinh ra để bảo vệ: mặc định sai ở đây nghĩa là một bài định để riêng tư trở thành công
    /// khai, và không ai biết cho tới khi quá muộn.
    /// </summary>
    [Fact]
    public async Task Q_D2_thieu_privacy_tra_400_chu_khong_mac_dinh_thanh_public()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        using var response = await client.CreatePostAsync(actor, new { body = "Quên mức riêng tư." });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(CreatePostRequestValidator.PrivacyRequired, errors["privacy"].Single());

        var row = await client.QueryRowAsync("SELECT count(*) AS n FROM content.posts WHERE author_id = $1", actor);
        Assert.Equal(0L, row!["n"]);
    }

    // AC-02 và AC-03 từng có một bản rút gọn ở đây (D5). Đã chuyển sang Content/PostContentRulesTests (B3, chốt Q-B3)
    // khi bản đầy đủ ra đời: hai test khẳng định cùng một điều là hai chỗ lệch được, và bản ở B3 khẳng định nhiều hơn
    // (bốn cách "rỗng", đúng MỘT key trong errors, số dòng posts không tăng).

    /// <summary>
    /// Field lạ → 400 (<c>UnmappedMemberHandling.Disallow</c> ở <c>Program.cs</c>). Quan trọng cho D7: hợp đồng cấm sửa
    /// ảnh ở GĐ2, và cơ chế chặn là chính cái này.
    /// </summary>
    [Fact]
    public async Task Field_la_trong_body_tra_400()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        using var response = await client.CreatePostAsync(
            actor, new { body = "Có field lạ.", privacy = "public", authorId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// <b>Tầng 2</b> — vai trò không có <c>post.create</c> → 403 qua <c>[RequirePermission]</c>. Khác
    /// <c>POST /media/uploads</c> (Q-D5, kiểm ở thân action) vì endpoint này chỉ có MỘT mức quyền nên khai được bằng
    /// attribute; test này canh chính chỗ khác nhau đó.
    /// </summary>
    [Fact]
    public async Task Vai_tro_thieu_post_create_tra_403()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        using var response = await client.CreatePostAsync(
            actor, new { body = "Không có quyền.", privacy = "public" }, role: "GUEST");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary><c>AC-04</c> — token hết hạn → 401, tầng 1 chặn trước mọi thứ khác.</summary>
    [Fact]
    public async Task AC_04_token_het_han_tra_401()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var expired = TestJwt.Create("USER", actor, issuedAt: DateTimeOffset.UtcNow.AddHours(-1));

        using var response = await client.CreatePostAsync(
            actor, new { body = "Token cũ.", privacy = "public" }, token: expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Tầng 1 với người hoàn toàn ẩn danh.</summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.Http.PostAsJsonAsync(
            "/api/v1/posts", new { body = "Không token.", privacy = "public" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
