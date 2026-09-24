using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// GĐ6 D6 — <c>POST /reports</c> (FR-019, Đ-6.12) qua API thật: bài dựng bằng <c>POST /posts</c>, quan hệ bằng
/// <c>socialgraph-v1</c>, chỉ trạng thái mà chưa endpoint nào ghi được (báo cáo <c>dismissed</c> — D7c; bài <c>hidden</c> — D7c)
/// mới đặt bằng SQL.
///
/// Hai ca canh luật chống dò (<c>REP-02</c>, <c>REP-07</c>) so CẢ <c>title</c>, <c>detail</c>, <c>type</c> giữa "không tồn tại"
/// và "không thấy được" — khác một trường là endpoint đã thành máy dò. Ca đồng thời <c>REP-C1</c> chạy 20 lượt liền trước khi tin
/// (Mục 1.3 luật 10).
///
/// Mỗi ca một người báo mới: hạn mức <c>report-create</c> phân vùng theo <c>sub</c>, nên các ca không ăn hạn mức của nhau.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportSubmissionTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static object BaoCao(Guid targetId, string targetType = "post", string reasonCode = "spam", string? detail = null) =>
        detail is null
            ? new { targetType, targetId, reasonCode }
            : new { targetType, targetId, reasonCode, detail };

    private static async Task<Guid> NguoiCoHoSoAsync(ModulesTestClient client)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = "Người dùng D6" });
        return id;
    }

    private static async Task<Guid> BaiAsync(ModulesTestClient client, Guid author, string privacy = "public") =>
        (await client.CreatePostOkAsync(author, new { body = "Bài để báo cáo.", privacy, mediaKeys = Array.Empty<object>() }))
        .PostId;

    private static async Task<JsonElement> DocJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<long> SoBaoCaoAsync(ModulesTestClient client, Guid reporter) =>
        (long)(await client.QueryRowAsync(
            "select count(*) as n from moderation.reports where reporter_id = $1", reporter))!["n"]!;

    /// <summary>Thân lỗi 404 rút gọn về đúng ba trường mà người dò so được.</summary>
    private static async Task<(string? Title, string? Detail, string? Type)> Than404Async(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await DocJsonAsync(response);
        return (root.GetProperty("title").GetString(), root.GetProperty("detail").GetString(),
            root.GetProperty("type").GetString());
    }

    [Fact]
    public async Task REP_01_bao_bai_cong_khai_201_bao_lai_200_cung_reportId_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        var nguoiBao = Guid.NewGuid();

        using var lan1 = await client.CreateReportAsync(nguoiBao, BaoCao(bai));
        Assert.Equal(HttpStatusCode.Created, lan1.StatusCode);
        Assert.Null(lan1.Headers.Location);
        var bienNhan1 = await DocJsonAsync(lan1);

        using var lan2 = await client.CreateReportAsync(nguoiBao, BaoCao(bai, reasonCode: "harassment"));
        Assert.Equal(HttpStatusCode.OK, lan2.StatusCode);
        var bienNhan2 = await DocJsonAsync(lan2);

        Assert.Equal(bienNhan1.GetProperty("reportId").GetGuid(), bienNhan2.GetProperty("reportId").GetGuid());
        Assert.Equal(bienNhan1.GetProperty("createdAt").GetDateTimeOffset(), bienNhan2.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal("open", bienNhan2.GetProperty("status").GetString());

        // Hợp đồng: biên nhận KHÔNG nhắc lại đối tượng — đúng ba trường.
        Assert.Equal(new[] { "createdAt", "reportId", "status" }, bienNhan1.EnumerateObject().Select(p => p.Name).Order());

        Assert.Equal(1, await SoBaoCaoAsync(client, nguoiBao));
        var row = await client.QueryRowAsync(
            "select reason_code, target_type, target_id from moderation.reports where reporter_id = $1", nguoiBao);
        Assert.Equal("spam", row!["reason_code"]);   // lần báo lại không đổi lý do của báo cáo đang mở
        Assert.Equal("post", row["target_type"]);
        Assert.Equal(bai, row["target_id"]);
    }

    [Fact]
    public async Task REP_02_bai_rieng_tu_khong_ton_tai_da_xoa_da_an_binh_luan_cung_mot_than_404()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var riengTu = await BaiAsync(client, tacGia, "private");
        var daXoa = await BaiAsync(client, tacGia);
        using (var xoa = await client.DeletePostAsync(tacGia, daXoa))
            Assert.Equal(HttpStatusCode.NoContent, xoa.StatusCode);
        var daAn = await BaiAsync(client, tacGia);
        await client.ExecuteSqlAsync(
            "update content.posts set status = 'hidden', hidden_reason = 'spam' where post_id = $1", daAn);
        var nguoiBao = Guid.NewGuid();

        using var khongTonTai = await client.CreateReportAsync(nguoiBao, BaoCao(Guid.NewGuid()));
        var mau = await Than404Async(khongTonTai);
        Assert.Equal("Không tìm thấy nội dung cần báo cáo.", mau.Detail);

        foreach (var body in new[]
        {
            BaoCao(riengTu),
            BaoCao(daXoa),
            BaoCao(daAn),
            BaoCao(Guid.NewGuid(), targetType: "comment"),   // bình luận không tồn tại (có provider từ bước 9 — trước đó: L-D13)
            BaoCao(Guid.NewGuid(), targetType: "user"),      // người không có hồ sơ
        })
        {
            using var response = await client.CreateReportAsync(nguoiBao, body);
            Assert.Equal(mau, await Than404Async(response));
        }

        Assert.Equal(0, await SoBaoCaoAsync(client, nguoiBao));
    }

    [Fact]
    public async Task REP_03_bao_bai_cua_minh_va_bao_chinh_minh_400_errors_targetId()
    {
        var client = new ModulesTestClient(factory);
        var toi = await NguoiCoHoSoAsync(client);
        var baiCuaToi = await BaiAsync(client, toi, "private");   // riêng tư: tác giả VẪN thấy → tới được bước "của mình"

        using var baoBai = await client.CreateReportAsync(toi, BaoCao(baiCuaToi));
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(baoBai);
        Assert.Equal(400, status);
        Assert.Equal(new[] { "Không thể báo cáo nội dung của chính mình." }, errors["targetId"]);

        using var baoMinh = await client.CreateReportAsync(toi, BaoCao(toi, targetType: "user"));
        (status, _, errors) = await ModulesTestClient.ReadProblemAsync(baoMinh);
        Assert.Equal(400, status);
        Assert.Equal(new[] { "Không thể báo cáo chính mình." }, errors["targetId"]);

        Assert.Equal(0, await SoBaoCaoAsync(client, toi));
    }

    public static TheoryData<string, string> BodySai => new()
    {
        { """{"targetType":"post","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"other"}""", "detail" },
        { """{"targetType":"post","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"other","detail":"   "}""", "detail" },
        { $$"""{"targetType":"post","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"spam","detail":"{{new string('a', 501)}}"}""", "detail" },
        { """{"targetType":"Post","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"spam"}""", "targetType" },
        { """{"targetType":"video","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"spam"}""", "targetType" },
        { """{"targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"spam"}""", "targetType" },
        { """{"targetType":"post","reasonCode":"spam"}""", "targetId" },
        { """{"targetType":"post","targetId":"00000000-0000-0000-0000-000000000000","reasonCode":"spam"}""", "targetId" },
        { """{"targetType":"post","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20","reasonCode":"abuse"}""", "reasonCode" },
        { """{"targetType":"post","targetId":"0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20"}""", "reasonCode" },
    };

    /// <summary>REP-04 và các biến thể sai dạng: 400 dưới đúng trường, TRƯỚC mọi I/O (id đích không tồn tại mà không ra 404).</summary>
    [Theory]
    [MemberData(nameof(BodySai))]
    public async Task REP_04_body_sai_400_dung_truong(string json, string truong)
    {
        var client = new ModulesTestClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await client.Http.SendAsync(request);

        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.True(errors.ContainsKey(truong), $"Thiếu errors.{truong}: {string.Join(", ", errors.Keys)}");
    }

    [Fact]
    public async Task REP_04b_detail_cat_khoang_trang_khi_luu_chi_khoang_trang_thanh_null()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var (bai1, bai2) = (await BaiAsync(client, tacGia), await BaiAsync(client, tacGia));
        var nguoiBao = Guid.NewGuid();

        using (var khac = await client.CreateReportAsync(nguoiBao, BaoCao(bai1, reasonCode: "other", detail: "  Lừa đảo.  ")))
            Assert.Equal(HttpStatusCode.Created, khac.StatusCode);
        using (var spam = await client.CreateReportAsync(nguoiBao, BaoCao(bai2, detail: "   ")))
            Assert.Equal(HttpStatusCode.Created, spam.StatusCode);

        Assert.Equal("Lừa đảo.", (await client.QueryRowAsync(
            "select detail from moderation.reports where reporter_id = $1 and target_id = $2", nguoiBao, bai1))!["detail"]);
        Assert.Null((await client.QueryRowAsync(
            "select detail from moderation.reports where reporter_id = $1 and target_id = $2", nguoiBao, bai2))!["detail"]);
    }

    /// <summary>
    /// REP-05: bài KHÁC NHAU cho mỗi lượt (cạm bẫy 3) và khẳng định lượt thứ 11 — không "có 429 ở đâu đó". Hạn mức chung 100/phút
    /// không chạm tới được trong 11 lượt, nên 429 này là của <c>report-create</c>.
    /// </summary>
    [Fact]
    public async Task REP_05_bao_cao_thu_11_trong_mot_phut_429()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = new List<Guid>();
        for (var i = 0; i < 11; i++)
            bai.Add(await BaiAsync(client, tacGia));
        var nguoiBao = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            using var ok = await client.CreateReportAsync(nguoiBao, BaoCao(bai[i]));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        using var thu11 = await client.CreateReportAsync(nguoiBao, BaoCao(bai[10]));
        Assert.Equal(HttpStatusCode.TooManyRequests, thu11.StatusCode);
        Assert.Equal(10, await SoBaoCaoAsync(client, nguoiBao));

        // Theo NGƯỜI, không theo IP: người khác vẫn báo được ngay trong cùng cửa sổ.
        using var nguoiKhac = await client.CreateReportAsync(Guid.NewGuid(), BaoCao(bai[10]));
        Assert.Equal(HttpStatusCode.Created, nguoiKhac.StatusCode);
    }

    [Fact]
    public async Task REP_06_bao_lai_sau_khi_bao_cao_cu_da_dismissed_201_bao_cao_moi()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        var nguoiBao = Guid.NewGuid();

        using var lan1 = await client.CreateReportAsync(nguoiBao, BaoCao(bai));
        var cu = (await DocJsonAsync(lan1)).GetProperty("reportId").GetGuid();

        // D7c chưa có: đóng báo cáo bằng SQL, đúng hình dạng ck_reports_decided.
        Assert.Equal(1, await client.ExecuteSqlAsync(
            "update moderation.reports set status = 'dismissed', resolver_id = $2, resolved_at = now() where id = $1",
            cu, Guid.NewGuid()));

        using var lan2 = await client.CreateReportAsync(nguoiBao, BaoCao(bai));
        Assert.Equal(HttpStatusCode.Created, lan2.StatusCode);
        Assert.NotEqual(cu, (await DocJsonAsync(lan2)).GetProperty("reportId").GetGuid());
        Assert.Equal(2, await SoBaoCaoAsync(client, nguoiBao));
    }

    [Fact]
    public async Task REP_07_bai_friends_cua_ban_201_cua_nguoi_la_404_cung_than_voi_khong_ton_tai()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var ban = await NguoiCoHoSoAsync(client);
        await client.MakeFriendsAsync(ban, tacGia);
        var bai = await BaiAsync(client, tacGia, "friends");

        using var cuaBan = await client.CreateReportAsync(ban, BaoCao(bai));
        Assert.Equal(HttpStatusCode.Created, cuaBan.StatusCode);

        var nguoiLa = Guid.NewGuid();
        using var cuaNguoiLa = await client.CreateReportAsync(nguoiLa, BaoCao(bai));
        using var khongTonTai = await client.CreateReportAsync(nguoiLa, BaoCao(Guid.NewGuid()));
        Assert.Equal(await Than404Async(khongTonTai), await Than404Async(cuaNguoiLa));
    }

    [Fact]
    public async Task Bao_tai_khoan_nguoi_khac_201()
    {
        var client = new ModulesTestClient(factory);
        var biBao = await NguoiCoHoSoAsync(client);
        var nguoiBao = Guid.NewGuid();

        using var response = await client.CreateReportAsync(
            nguoiBao, BaoCao(biBao, targetType: "user", reasonCode: "harassment"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("user", (await client.QueryRowAsync(
            "select target_type from moderation.reports where reporter_id = $1", nguoiBao))!["target_type"]);
    }

    /// <summary>Tầng 2: vai trò không có <c>report.create</c> (<c>GUEST</c> — không dòng <c>role_permissions</c> nào) → 403.</summary>
    [Fact]
    public async Task Vai_tro_khong_co_report_create_403()
    {
        var client = new ModulesTestClient(factory);
        using var response = await client.CreateReportAsync(Guid.NewGuid(), BaoCao(Guid.NewGuid()), role: "GUEST");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Khong_token_401()
    {
        var client = new ModulesTestClient(factory);
        using var response = await client.Http.PostAsJsonAsync("/api/v1/reports", BaoCao(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// REP-C1 ⭐: mười lượt giống hệt, song song, từ một người → MỘT dòng; đúng một 201, chín 200 cùng <c>reportId</c>; không 500.
    /// Đỏ khi bỏ vế <c>WHERE</c> của <c>ON CONFLICT</c> (500 <c>42P10</c>) hay khi thay index bằng "đọc rồi mới ghi" (hai 201).
    /// </summary>
    [Fact]
    public async Task REP_C1_muoi_luot_song_song_mot_dong_mot_201_chin_200()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        var nguoiBao = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => client.CreateReportAsync(nguoiBao, BaoCao(bai))));
        try
        {
            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(9, statuses.Count(s => s == HttpStatusCode.OK));

            var ids = new HashSet<Guid>();
            foreach (var response in responses)
                ids.Add((await DocJsonAsync(response)).GetProperty("reportId").GetGuid());
            Assert.Single(ids);
            Assert.Equal(1, await SoBaoCaoAsync(client, nguoiBao));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }
}
