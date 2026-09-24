using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Moderation.Domain;
using SocialApp.Modules.Moderation.Infrastructure;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// GĐ6 D7b — <c>GET /reports</c>, <c>GET /reports/{reportId}</c> (Đ-6.13, Mục 8.1) trên Postgres + Redis thật.
///
/// Redis THẬT là bắt buộc: hai endpoint mang <c>[PrivilegedEndpoint]</c> fail-closed (Đ-6.8) — với Redis cổng 1 mặc định của
/// <see cref="ModulesApiFactory"/> mọi request có token đều 503 trước tầng 2 (cùng lý do <c>AdminUsersTests</c>).
///
/// Báo cáo tạo qua <c>POST /reports</c> thật khi đường đó cho phép; chỉ trạng thái chưa endpoint nào ghi được mới đặt bằng SQL:
/// báo cáo trên bài người báo không thấy được (D6 trả 404), báo cáo đã đóng (D7c), đối tượng không còn tồn tại. Các ca dùng chung
/// một database, nên ca hàng đợi lọc theo đối tượng của chính mình (phân trang đúng 20/20/5 ở <see cref="ReportQueuePagingTests"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportQueueTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);

        // Kết nối Redis của APP mở ở nền lúc host khởi động — chưa xong thì kiểm thu hồi ra Unknown và endpoint đặc quyền 503.
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    /// <summary>Trả pool của database riêng (cạm bẫy 7 Mục 3 hướng dẫn khối A+C).</summary>
    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    private static async Task<HttpResponseMessage> GetAsync(
        ModulesTestClient client, string path, string role = "MODERATOR", Guid? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = ModulesTestClient.Bearer(caller ?? Guid.NewGuid(), role);
        return await client.Http.SendAsync(request);
    }

    private static async Task<JsonElement> GetOkAsync(ModulesTestClient client, string path, string role = "MODERATOR")
    {
        using var response = await GetAsync(client, path, role);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} → {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<Guid> NguoiCoHoSoAsync(ModulesTestClient client, string ten = "Người dùng D7b")
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = ten });
        return id;
    }

    private static async Task<Guid> BaiAsync(ModulesTestClient client, Guid author, string privacy = "public", object[]? media = null) =>
        (await client.CreatePostOkAsync(
            author, new { body = $"Bài {privacy} bị báo cáo.", privacy, mediaKeys = media ?? Array.Empty<object>() })).PostId;

    /// <summary>Báo cáo qua API thật, mỗi lần một người báo mới (hạn mức <c>report-create</c> theo người). Trả <c>reportId</c>.</summary>
    private static async Task<Guid> BaoCaoAsync(ModulesTestClient client, Guid target, string reasonCode = "spam", Guid? reporter = null)
    {
        using var response = await client.CreateReportAsync(
            reporter ?? Guid.NewGuid(), new { targetType = "post", targetId = target, reasonCode });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("reportId").GetGuid();
    }

    /// <summary>Báo cáo đặt bằng SQL — chỉ cho trạng thái mà <c>POST /reports</c> không cho tạo (xem phần đầu lớp).</summary>
    private static async Task<Guid> BaoCaoSqlAsync(ModulesTestClient client, string targetType, Guid target, string reasonCode = "spam")
    {
        var id = Uuid7.New();
        await client.ExecuteSqlAsync(
            "insert into moderation.reports (id, reporter_id, target_type, target_id, reason_code, status, created_at, updated_at) " +
            "values ($1, $2, $3, $4, $5, 'open', now(), now())",
            id, Guid.NewGuid(), targetType, target, reasonCode);
        return id;
    }

    /// <summary>Mọi dòng hàng đợi (đi hết các trang, <c>limit=50</c>), theo đúng thứ tự trả về.</summary>
    private static async Task<List<JsonElement>> CaHangDoiAsync(ModulesTestClient client)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var page = await GetOkAsync(client, "/api/v1/reports?limit=50" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
            items.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        return items;
    }

    private static Guid TargetId(JsonElement item) => item.GetProperty("target").GetProperty("id").GetGuid();

    /// <summary>
    /// <c>QUE-01</c> — ba người báo bài P (spam, spam, violence), một người báo bài Q: hai dòng, P trước (cũ hơn), <c>reportCount = 3</c>,
    /// <c>reasons = { spam: 2, violence: 1 }</c> (không có key lý do đếm 0), <c>reportId</c> là báo cáo cũ nhất của P.
    /// </summary>
    [Fact]
    public async Task QUE_01_gom_theo_doi_tuong_dem_theo_ly_do_dai_dien_la_bao_cao_cu_nhat()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var (p, q) = (await BaiAsync(client, tacGia), await BaiAsync(client, tacGia));
        var cuNhat = await BaoCaoAsync(client, p, "spam");
        await BaoCaoAsync(client, p, "spam");
        await BaoCaoAsync(client, p, "violence");
        await BaoCaoAsync(client, q, "harassment");

        var mine = (await CaHangDoiAsync(client)).Where(i => TargetId(i) == p || TargetId(i) == q).ToList();

        Assert.Equal([p, q], mine.Select(TargetId));
        var dongP = mine[0];
        Assert.Equal(cuNhat, dongP.GetProperty("reportId").GetGuid());
        Assert.Equal("post", dongP.GetProperty("target").GetProperty("type").GetString());
        Assert.Equal(3, dongP.GetProperty("reportCount").GetInt32());
        Assert.Equal(
            new Dictionary<string, int> { ["spam"] = 2, ["violence"] = 1 },
            dongP.GetProperty("reasons").EnumerateObject().ToDictionary(r => r.Name, r => r.Value.GetInt32()));
        Assert.Equal(1, mine[1].GetProperty("reportCount").GetInt32());

        var createdAt = (DateTime)(await client.QueryRowAsync(
            "select created_at from moderation.reports where id = $1", cuNhat))!["created_at"]!;
        Assert.Equal(new DateTimeOffset(createdAt, TimeSpan.Zero), dongP.GetProperty("firstReportedAt").GetDateTimeOffset());
    }

    /// <summary>Báo cáo đã đóng rời khỏi hàng đợi; đối tượng còn báo cáo mở khác thì vẫn một dòng, đếm chỉ báo cáo mở.</summary>
    [Fact]
    public async Task QUE_01b_bao_cao_da_dong_khong_vao_hang_doi()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var (conMo, daDongHet) = (await BaiAsync(client, tacGia), await BaiAsync(client, tacGia));
        var dong1 = await BaoCaoAsync(client, conMo);
        var mo = await BaoCaoAsync(client, conMo, "nudity");
        var dong2 = await BaoCaoAsync(client, daDongHet);
        await DongBangSqlAsync(client, "dismissed", Guid.NewGuid(), dong1, dong2);

        var mine = (await CaHangDoiAsync(client)).Where(i => TargetId(i) == conMo || TargetId(i) == daDongHet).ToList();

        var dong = Assert.Single(mine);
        Assert.Equal(conMo, TargetId(dong));
        Assert.Equal(mo, dong.GetProperty("reportId").GetGuid());
        Assert.Equal(1, dong.GetProperty("reportCount").GetInt32());
    }

    /// <summary>
    /// <c>QUE-03</c> — chi tiết bài <c>private</c> của người khác, bài đã bị ẩn, bài đã xóa mềm (bằng API xóa): 200, có <c>body</c> và
    /// tác giả — Moderator phải thấy mới quyết được. Đây là đường DUY NHẤT họ đọc được nội dung không công khai.
    /// </summary>
    [Fact]
    public async Task QUE_03_chi_tiet_bai_rieng_tu_bi_an_da_xoa_van_co_noi_dung()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client, "Bình Minh");
        var riengTu = await BaiAsync(client, tacGia, "private");
        var biAn = await BaiAsync(client, tacGia);
        var daXoa = await BaiAsync(client, tacGia);
        var rRiengTu = await BaoCaoSqlAsync(client, "post", riengTu);   // POST /reports trả 404 cho bài người báo không thấy
        var rBiAn = await BaoCaoAsync(client, biAn);
        var rDaXoa = await BaoCaoAsync(client, daXoa);
        await AnAsync(biAn);
        using (var xoa = await client.DeletePostAsync(tacGia, daXoa))
            Assert.Equal(HttpStatusCode.NoContent, xoa.StatusCode);

        foreach (var (reportId, postId, status) in new[] { (rRiengTu, riengTu, "published"), (rBiAn, biAn, "hidden"), (rDaXoa, daXoa, "deleted") })
        {
            var target = (await GetOkAsync(client, $"/api/v1/reports/{reportId}")).GetProperty("target");
            Assert.Equal(status, target.GetProperty("status").GetString());
            Assert.Equal(postId, target.GetProperty("id").GetGuid());
            Assert.Equal(postId, target.GetProperty("postId").GetGuid());
            Assert.StartsWith("Bài", target.GetProperty("body").GetString());
            Assert.Equal("Bình Minh", target.GetProperty("author").GetProperty("displayName").GetString());
            Assert.Equal(tacGia, target.GetProperty("author").GetProperty("userId").GetGuid());
        }
    }

    /// <summary>
    /// <c>QUE-04</c> — JSON của chi tiết không có key <c>reporterId</c> ở bất kỳ độ sâu nào, và id người báo không xuất hiện ở đâu
    /// trong thân (kể cả dưới tên trường khác). Hai người báo, một mở một đã đóng — cả <c>openReports</c> lẫn <c>history</c> đều bị soi.
    /// </summary>
    [Fact]
    public async Task QUE_04_chi_tiet_khong_lo_nguoi_bao_o_bat_ky_do_sau_nao()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        var (nguoiBao1, nguoiBao2) = (Guid.NewGuid(), Guid.NewGuid());
        var r1 = await BaoCaoAsync(client, bai, reporter: nguoiBao1);
        await DongBangSqlAsync(client, "dismissed", Guid.NewGuid(), r1);
        await BaoCaoAsync(client, bai, "nudity", nguoiBao2);

        using var response = await GetAsync(client, $"/api/v1/reports/{r1}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(nguoiBao1.ToString("D"), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nguoiBao2.ToString("D"), raw, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(TenTruong(JsonDocument.Parse(raw).RootElement)
            .Where(n => n.Contains("reporter", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// <c>QUE-05</c> — số câu SQL của chi tiết bài 1 ảnh và bài 4 ảnh BẰNG nhau: ảnh đọc một lô, URL ký cục bộ. Chạy mỗi đường một lần
    /// trước khi đếm để cache quyền đã nạp — thứ cần đếm là đường của endpoint, không phải lần nạp cache đầu.
    /// </summary>
    [Fact]
    public async Task QUE_05_so_cau_SQL_cua_chi_tiet_khong_doi_theo_so_anh()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var motAnh = await BaiAsync(client, tacGia, media: [client.PutPostObject(tacGia)]);
        var bonAnh = await BaiAsync(client, tacGia, media: [.. Enumerable.Range(0, 4).Select(_ => client.PutPostObject(tacGia))]);
        var (r1, r4) = (await BaoCaoAsync(client, motAnh), await BaoCaoAsync(client, bonAnh));
        const string role = "MODERATOR";
        await GetOkAsync(client, $"/api/v1/reports/{r1}", role);
        await GetOkAsync(client, $"/api/v1/reports/{r4}", role);

        using var counter = new SqlCommandCounter(factory.ConnectionString);
        var mot = await GetOkAsync(client, $"/api/v1/reports/{r1}", role);
        var soCauMot = counter.Statements.Count;
        counter.Reset();
        var bon = await GetOkAsync(client, $"/api/v1/reports/{r4}", role);
        var soCauBon = counter.Statements.Count;

        Assert.Equal(1, mot.GetProperty("target").GetProperty("media").GetArrayLength());
        Assert.Equal(4, bon.GetProperty("target").GetProperty("media").GetArrayLength());
        Assert.All(bon.GetProperty("target").GetProperty("media").EnumerateArray(),
            m => Assert.StartsWith("http", m.GetProperty("url").GetString()));
        Assert.True(soCauMot > 0);
        Assert.Equal(soCauMot, soCauBon);
    }

    /// <summary>
    /// <c>QUE-06</c> <i>(đề xuất)</i> — lịch sử và báo cáo mở: mở chi tiết bằng báo cáo ĐÃ ĐÓNG vẫn được, thấy báo cáo mở mới của
    /// cùng đối tượng; lần quyết cũ là một dòng <c>outcome</c> (L-D15) kèm người quyết, mốc, ghi chú.
    /// </summary>
    [Fact]
    public async Task QUE_06_lich_su_quyet_dinh_va_bao_cao_mo_cung_doi_tuong()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var bai = await BaiAsync(client, tacGia);
        var cu1 = await BaoCaoAsync(client, bai);
        var cu2 = await BaoCaoAsync(client, bai, "violence");
        var moderator = Guid.NewGuid();
        var resolvedAt = await DongBangSqlAsync(client, "dismissed", moderator, cu1, cu2);
        var moi = await BaoCaoAsync(client, bai, "nudity");

        foreach (var mo in new[] { cu1, moi })
        {
            var detail = await GetOkAsync(client, $"/api/v1/reports/{mo}");

            Assert.Equal(mo, detail.GetProperty("reportId").GetGuid());
            var open = Assert.Single(detail.GetProperty("openReports").EnumerateArray());
            Assert.Equal(moi, open.GetProperty("reportId").GetGuid());
            Assert.Equal("nudity", open.GetProperty("reasonCode").GetString());
            var history = Assert.Single(detail.GetProperty("history").EnumerateArray());
            Assert.Equal("dismissed", history.GetProperty("outcome").GetString());
            Assert.Equal(moderator, history.GetProperty("resolverId").GetGuid());
            Assert.Equal(resolvedAt, history.GetProperty("resolvedAt").GetDateTimeOffset());
            Assert.Equal("Không vi phạm tiêu chuẩn.", history.GetProperty("note").GetString());
        }
    }

    /// <summary><c>QUE-07</c> <i>(đề xuất)</i> — đối tượng không còn trong bảng nào: 200, <c>status: deleted</c>, mọi trường nội dung null.</summary>
    [Fact]
    public async Task QUE_07_doi_tuong_bien_mat_200_status_deleted()
    {
        var client = new ModulesTestClient(factory);
        var target = Guid.NewGuid();
        var reportId = await BaoCaoSqlAsync(client, "post", target);

        var t = (await GetOkAsync(client, $"/api/v1/reports/{reportId}")).GetProperty("target");

        Assert.Equal("deleted", t.GetProperty("status").GetString());
        Assert.Equal(target, t.GetProperty("id").GetGuid());
        foreach (var field in new[] { "author", "body", "postId", "createdAt", "editedAt" })
            Assert.Equal(JsonValueKind.Null, t.GetProperty(field).ValueKind);
        Assert.Equal(0, t.GetProperty("media").GetArrayLength());
    }

    /// <summary>Báo cáo tài khoản: ảnh chụp là hồ sơ — tên, tiểu sử, <c>status: active</c>, <c>postId: null</c>.</summary>
    [Fact]
    public async Task Chi_tiet_bao_cao_tai_khoan_la_ho_so()
    {
        var client = new ModulesTestClient(factory);
        var nguoiBiBao = Guid.NewGuid();
        await client.PutProfileOkAsync(nguoiBiBao, new { displayName = "Người bị báo", bio = "Tiểu sử bị báo cáo." });
        using (var bao = await client.CreateReportAsync(Guid.NewGuid(), new { targetType = "user", targetId = nguoiBiBao, reasonCode = "harassment" }))
            Assert.Equal(HttpStatusCode.Created, bao.StatusCode);
        var reportId = (Guid)(await client.QueryRowAsync(
            "select id from moderation.reports where target_id = $1", nguoiBiBao))!["id"]!;

        var t = (await GetOkAsync(client, $"/api/v1/reports/{reportId}")).GetProperty("target");

        Assert.Equal(("user", "active"), (t.GetProperty("type").GetString(), t.GetProperty("status").GetString()));
        Assert.Equal("Tiểu sử bị báo cáo.", t.GetProperty("body").GetString());
        Assert.Equal("Người bị báo", t.GetProperty("author").GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("postId").ValueKind);
    }

    [Fact]
    public async Task Bao_cao_khong_ton_tai_404()
    {
        var client = new ModulesTestClient(factory);
        using var response = await GetAsync(client, $"/api/v1/reports/{Guid.NewGuid()}");
        var (status, _, _) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(404, status);
    }

    [Theory]
    [InlineData("/api/v1/reports?status=resolved", "status")]
    [InlineData("/api/v1/reports?status=OPEN", "status")]
    [InlineData("/api/v1/reports?limit=0", "limit")]
    [InlineData("/api/v1/reports?limit=51", "limit")]
    [InlineData("/api/v1/reports?cursor=rac", "cursor")]
    [InlineData("/api/v1/reports/abc", "reportId")]
    public async Task Tham_so_sai_400_dung_truong(string path, string field)
    {
        var client = new ModulesTestClient(factory);
        using var response = await GetAsync(client, path);
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.True(errors.ContainsKey(field), $"Thiếu errors.{field}: {string.Join(", ", errors.Keys)}");
    }

    /// <summary>MODERATOR và ADMIN (hai vai trò hệ thống có <c>report.resolve</c>) đọc được cả hai đường.</summary>
    [Theory]
    [InlineData("MODERATOR")]
    [InlineData("ADMIN")]
    public async Task Vai_tro_co_report_resolve_200(string role)
    {
        var client = new ModulesTestClient(factory);
        var reportId = await BaoCaoSqlAsync(client, "post", Guid.NewGuid());

        await GetOkAsync(client, "/api/v1/reports", role);
        await GetOkAsync(client, $"/api/v1/reports/{reportId}", role);
    }

    /// <summary>
    /// <c>TC-A06-queue</c> qua endpoint thật + <c>AUD-03</c> (Mục 10.1): USER gọi hàng đợi / chi tiết năm lần trong một phút (chi tiết:
    /// mỗi lần một id khác) → năm lần 403, ĐÚNG MỘT dòng <c>access.denied</c> mang route template — khử trùng của C4 theo route, không
    /// theo đường cụ thể. Bản probe của C4 ở <c>PrivilegedEndpointAuditTests</c>; đây là bản trên controller thật.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/reports", "api/v1/reports")]
    [InlineData("/api/v1/reports/{0}", "api/v1/reports/{reportId}")]
    public async Task AUD_03_user_nam_lan_403_dung_mot_dong_access_denied(string pathFormat, string routeTemplate)
    {
        var client = new ModulesTestClient(factory);
        var user = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            using var response = await GetAsync(client, string.Format(pathFormat, Guid.NewGuid()), "USER", user);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var row = await client.QueryRowAsync(
            "select count(*) as n, min(metadata->>'routeTemplate') as route from moderation.audit_logs " +
            "where action = 'access.denied' and actor_id = $1", user);
        Assert.Equal(1L, row!["n"]);
        Assert.Equal(routeTemplate, row["route"]);
    }

    /// <summary>Đóng báo cáo bằng SQL — D7c chưa có. Cùng người, cùng mốc, cùng ghi chú: hình dạng MỘT lần quyết của D7c. Trả mốc.</summary>
    private static async Task<DateTimeOffset> DongBangSqlAsync(ModulesTestClient client, string status, Guid resolver, params Guid[] ids)
    {
        var at = DateTimeOffset.UtcNow;
        at = at.AddTicks(-(at.Ticks % 10));   // Postgres giữ tới micro giây
        foreach (var id in ids)
            Assert.Equal(1, await client.ExecuteSqlAsync(
                "update moderation.reports set status = $1, resolver_id = $2, resolved_at = $3, " +
                "resolution_note = 'Không vi phạm tiêu chuẩn.', updated_at = $3 where id = $4",
                status, resolver, at, id));
        return at;
    }

    /// <summary>Ẩn bài qua hợp đồng C2 trong transaction của Moderation — khuôn <c>HiddenPostTests</c>.</summary>
    private async Task AnAsync(Guid postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.Equal(HideOutcome.Hidden, await scope.ServiceProvider.GetRequiredService<IModerationTargets>()
            .HideAsync(tx.GetDbTransaction(), new ModerationTarget(ModerationTargetType.Post, postId), ReasonCodes.Spam));
        await tx.CommitAsync();
    }

    private static IEnumerable<string> TenTruong(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => TenTruong(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(TenTruong),
        _ => [],
    };
}
