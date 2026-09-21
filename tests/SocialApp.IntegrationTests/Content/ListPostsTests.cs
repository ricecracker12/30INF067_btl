using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D6 — <c>GET /users/{userId}/posts</c>: cursor keyset (Đ-2.11) và BR-02 lọc <b>trong câu truy vấn</b> (Mục 7.4).
///
/// Hình dạng phân trang ở đây là thứ GĐ4 dùng lại nguyên cho feed, nên mọi khẳng định về nó đáng giá gấp đôi: sai bây
/// giờ thì sai ở hai giai đoạn.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ListPostsTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Tác giả đã onboarding + <paramref name="count"/> bài <c>public</c>, cũ nhất trước.</summary>
    private static async Task<Guid> ArrangeAuthorAsync(ModulesTestClient client, int count, string privacy = "public")
    {
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Tác giả" });
        for (var i = 0; i < count; i++)
            await client.CreatePostOkAsync(author, new { body = $"Bài số {i}.", privacy });
        return author;
    }

    private static string CursorQuery(string cursor) => $"?cursor={Uri.EscapeDataString(cursor)}";

    /// <summary>
    /// <c>PAGE-01</c> — 25 bài, <c>limit=20</c>: trang 1 có 20 item + <c>nextCursor</c>; trang 2 có đủ 5 và
    /// <c>nextCursor: null</c>.
    ///
    /// Ba khẳng định ngoài số lượng: thứ tự MỚI NHẤT TRƯỚC (bài số 24 đứng đầu), hai trang KHÔNG giao nhau, và hợp lại
    /// đủ 25 bài — thiếu một bài giữa hai trang là lỗi keyset kinh điển mà chỉ đếm số lượng thì không thấy.
    /// </summary>
    [Fact]
    public async Task PAGE_01_hai_trang_du_25_bai_khong_trung_khong_thieu()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 25);

        var page1 = await client.ListPostsOkAsync(author, author, "?limit=20");
        Assert.Equal(20, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal("Bài số 24.", page1.Items[0].Body);

        var page2 = await client.ListPostsOkAsync(author, author, $"?limit=20&cursor={Uri.EscapeDataString(page1.NextCursor)}");
        Assert.Equal(5, page2.Items.Count);
        Assert.Null(page2.NextCursor);
        Assert.Equal("Bài số 0.", page2.Items[^1].Body);

        var all = page1.Items.Concat(page2.Items).Select(p => p.PostId).ToList();
        Assert.Equal(25, all.Distinct().Count());
    }

    /// <summary>
    /// <c>nextCursor</c> ra JSON là <c>null</c> khi hết dữ liệu, <b>không phải chuỗi rỗng</b> (Đ-2.11). Khẳng định trên
    /// JSON thô: FE kiểm <c>if (nextCursor)</c> thì <c>""</c> cũng falsy nên bản sai tình cờ chạy đúng — cho tới khi ai
    /// đó viết <c>if (nextCursor !== null)</c>, và lúc đó vòng lặp tải trang không bao giờ dừng.
    /// </summary>
    [Fact]
    public async Task nextCursor_la_null_chu_khong_phai_chuoi_rong_khi_het_du_lieu()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 2);

        using var response = await client.ListPostsAsync(author, author);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"nextCursor\":null", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>PAGE-03</c> — bài mới chèn vào GIỮA hai lần gọi. Đây là test phân biệt keyset với <c>OFFSET</c>, và là lý do
    /// Đ-2.11 chọn keyset:
    /// <list type="bullet">
    /// <item><b>Không nhân đôi</b>: trang 2 không chứa bài nào của trang 1. Với <c>OFFSET 20</c>, bài mới đẩy mọi thứ
    /// xuống một dòng nên bài thứ 20 của trang 1 xuất hiện lại ở đầu trang 2.</item>
    /// <item><b>Không nhảy cóc</b>: đủ 5 bài còn lại, không thiếu bài nào.</item>
    /// <item><b>Bài MỚI không xuất hiện</b>: nó mới hơn cursor nên đứng TRƯỚC vị trí đang đọc — đúng bản chất "neo vào
    /// một vị trí, không vào một số đếm".</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task PAGE_03_bai_moi_chen_giua_hai_lan_goi_khong_nhan_doi_khong_nhay_coc()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 25);

        var page1 = await client.ListPostsOkAsync(author, author, "?limit=20");
        var moi = await client.CreatePostOkAsync(author, new { body = "Bài chèn giữa chừng.", privacy = "public" });

        var page2 = await client.ListPostsOkAsync(author, author, $"?limit=20&cursor={Uri.EscapeDataString(page1.NextCursor!)}");

        Assert.Equal(5, page2.Items.Count);
        Assert.Empty(page2.Items.Select(p => p.PostId).Intersect(page1.Items.Select(p => p.PostId)));
        Assert.DoesNotContain(moi.PostId, page2.Items.Select(p => p.PostId));
    }

    /// <summary>
    /// <c>PAGE-02</c> — cursor rác → <b>400</b> <c>errors.cursor</c>, KHÔNG âm thầm trả trang đầu. Trả trang đầu thì
    /// người dùng cuộn mãi không hết, và không có gì báo là cursor đã hỏng.
    ///
    /// Ca cuối là cursor mang offset <c>+07:00</c>: nó giải mã ĐƯỢC (quy về UTC) nên phải ra <b>200</b>, không 500 —
    /// thiếu <c>ToUniversalTime()</c> thì Npgsql ném và một chuỗi client sửa tay thành lỗi máy chủ. Xem
    /// <c>PostCursorTests.Cursor_mang_offset_khac_0_…</c> về chỗ lệch với bảng Mục 0.
    /// </summary>
    [Fact]
    public async Task PAGE_02_cursor_rac_tra_400_con_cursor_lech_mui_gio_van_200()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 3);

        foreach (var rac in new[] { "abc", "!!!", "eHx5" })
        {
            using var response = await client.ListPostsAsync(author, author, CursorQuery(rac));
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Equal(ProblemTitles.BadRequest, title);
            Assert.Equal(ListUserPostsQueryValidator.CursorInvalid, errors["cursor"].Single());
        }

        var lechMuiGio = new PostCursor(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)), Guid.NewGuid()).Encode();
        using var hopLe = await client.ListPostsAsync(author, author, CursorQuery(lechMuiGio));
        Assert.Equal(HttpStatusCode.OK, hopLe.StatusCode);
    }

    /// <summary><c>limit</c> ngoài <c>1..50</c> hoặc sai kiểu → 400 <c>errors.limit</c>, không kẹp về biên.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task limit_ngoai_khoang_hoac_sai_kieu_tra_400_duoi_key_limit(string limit)
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 1);

        using var response = await client.ListPostsAsync(author, author, $"?limit={limit}");
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("limit", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>Không gửi <c>limit</c> → mặc định 20 (hợp đồng ghi <c>default: 20</c>), không phải 0 và không phải "hết".</summary>
    [Fact]
    public async Task Khong_gui_limit_thi_mac_dinh_20()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 22);

        var page = await client.ListPostsOkAsync(author, author);

        Assert.Equal(ListUserPostsQuery.DefaultLimit, page.Items.Count);
        Assert.NotNull(page.NextCursor);
    }

    /// <summary>
    /// Người dùng không tồn tại (hoặc chưa có bài) → <c>items: []</c>, <c>nextCursor: null</c> — <b>không 404</b>. Trả
    /// 404 ở đây là biến endpoint thành máy dò "id này có tồn tại không".
    /// </summary>
    [Fact]
    public async Task Nguoi_dung_khong_ton_tai_tra_mang_rong_chu_khong_404()
    {
        var client = new ModulesTestClient(factory);

        var page = await client.ListPostsOkAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    /// <summary>
    /// <b>BR-02 lọc TRONG câu truy vấn</b>, không sau khi đã lấy đủ <c>limit</c> dòng — cạm bẫy số một của D6.
    ///
    /// Dựng 5 bài <c>public</c> xen 5 bài <c>private</c>, rồi người lạ xin <c>limit=5</c>. Lọc trong truy vấn → đúng 5
    /// bài <c>public</c>. Lọc SAU khi <c>Take(5)</c> → chỉ còn 2–3 bài, trang thiếu hụt ngẫu nhiên và FE tưởng hết dữ
    /// liệu. Tác giả xin cùng trang đó thấy đủ 5 bài đầu (gồm cả <c>private</c>) — cùng câu truy vấn, khác người đọc.
    /// </summary>
    [Fact]
    public async Task BR02_loc_trong_cau_truy_van_nen_trang_khong_bi_thieu_hut()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Tác giả" });
        for (var i = 0; i < 10; i++)
            await client.CreatePostOkAsync(
                author, new { body = $"Bài {i}.", privacy = i % 2 == 0 ? "public" : "private" });

        var nguoiLa = await client.ListPostsOkAsync(Guid.NewGuid(), author, "?limit=5");
        Assert.Equal(5, nguoiLa.Items.Count);
        Assert.All(nguoiLa.Items, p => Assert.Equal(Modules.Content.Domain.PostPrivacy.Public, p.Privacy));

        var tacGia = await client.ListPostsOkAsync(author, author, "?limit=5");
        Assert.Equal(5, tacGia.Items.Count);
        Assert.Contains(tacGia.Items, p => p.Privacy == Modules.Content.Domain.PostPrivacy.Private);
    }

    /// <summary>
    /// Bài đã xóa mềm không nằm trong danh sách (Đ-2.10) — global query filter, không phải lọc tay.
    /// </summary>
    [Fact]
    public async Task Bai_da_xoa_mem_khong_nam_trong_danh_sach()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 3);
        var truoc = await client.ListPostsOkAsync(author, author);
        var xoa = truoc.Items[0].PostId;

        await client.QueryRowAsync(
            "UPDATE content.posts SET status = 'deleted', deleted_at = now() WHERE post_id = $1 RETURNING post_id", xoa);

        var sau = await client.ListPostsOkAsync(author, author);

        Assert.Equal(2, sau.Items.Count);
        Assert.DoesNotContain(xoa, sau.Items.Select(p => p.PostId));
    }

    /// <summary>
    /// <b>Đ-2.3 — một lời gọi <c>IUserDirectory</c> cho CẢ TRANG.</b> Đếm câu SQL chạm <c>profile.profiles</c> trong
    /// một request trang 20 bài: phải đúng <b>1</b>. Gọi theo từng bài thì con số là 20 và không có gì khác trong hệ
    /// thống báo động — N+1 ở endpoint này chỉ lộ ra ở k6 của GĐ4 (<c>PERF-01</c>).
    ///
    /// Phải nâng mức log EF cho riêng app của test: <c>appsettings</c> đặt <c>Microsoft.EntityFrameworkCore</c> ở
    /// <c>Warning</c> (chống nhiễu), mà <c>Executed DbCommand</c> là <c>Information</c> — không nâng thì sink rỗng và
    /// test xanh vì <b>không đếm được gì</b>, đúng loại lưới giả.
    /// </summary>
    [Fact]
    public async Task Mot_trang_chi_ton_mot_cau_SQL_doc_profile_profiles()
    {
        var client = new ModulesTestClient(factory);
        var author = await ArrangeAuthorAsync(client, 20);

        var logs = new CapturingLogSink();
        await using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore", "Information");
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(logs));
        });
        using var http = app.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{author:D}/posts?limit=20");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sql = logs.Events.Select(e => e.RenderMessage()).ToList();

        // Canh gác chống lưới giả: nếu không bắt được câu SQL nào thì phép đếm bên dưới vô nghĩa.
        Assert.Contains(sql, line => line.Contains("content.posts", StringComparison.Ordinal));
        Assert.Single(sql.Where(line => line.Contains("profile.profiles", StringComparison.Ordinal)));
        Assert.Single(sql.Where(line => line.Contains("content.media_attachments", StringComparison.Ordinal)));
    }

    /// <summary>Tầng 1 và tầng 2 cho endpoint danh sách.</summary>
    [Fact]
    public async Task An_danh_401_va_vai_tro_thieu_quyen_403()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();

        using var anDanh = await client.Http.GetAsync($"/api/v1/users/{author:D}/posts");
        Assert.Equal(HttpStatusCode.Unauthorized, anDanh.StatusCode);

        using var khach = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{author:D}/posts");
        khach.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), "GUEST");
        using var thieuQuyen = await client.Http.SendAsync(khach);
        Assert.Equal(HttpStatusCode.Forbidden, thieuQuyen.StatusCode);
    }
}
