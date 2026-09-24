using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// GĐ6 D7c — <c>PATCH /reports/{reportId}</c> và khôi phục (Đ-6.13, bốn AC của US-019) trên Postgres + Redis thật, mọi bước qua API:
/// bài qua <c>POST /posts</c>, báo cáo qua <c>POST /reports</c>, đọc lại qua <c>GET /posts</c> và <c>GET /reports</c>. Chỉ báo cáo mà
/// API không cho tạo (báo cáo thứ hai cho bài đã ẩn) mới đặt bằng SQL.
///
/// Event đọc bằng handler ghi lại (khuôn <c>SocialGraphEventsTests</c>) sau <c>DrainEventsAsync</c> — không đếm metric: metric là số
/// của cả process, các lớp khác cùng chạy làm lệch.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DecideReportTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        factory.UseTestServices(services =>
        {
            services.AddSingleton<Recorded>();
            services.AddIntegrationEventHandler<ContentHidden, RecordingHandler>();
        });
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

    private static async Task<Guid> NguoiCoHoSoAsync(ModulesTestClient client)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = "Người dùng D7c" });
        return id;
    }

    private static async Task<Guid> BaiAsync(ModulesTestClient client, Guid author, string body = "Bài bị báo cáo.") =>
        (await client.CreatePostOkAsync(author, new { body, privacy = "public", mediaKeys = Array.Empty<object>() })).PostId;

    private static async Task<Guid> BaoCaoAsync(
        ModulesTestClient client, Guid target, string reasonCode = "spam", string targetType = "post", string? detail = null)
    {
        using var response = await client.CreateReportAsync(Guid.NewGuid(), detail is null
            ? new { targetType, targetId = target, reasonCode }
            : new { targetType, targetId = target, reasonCode, detail });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await DocAsync(response)).GetProperty("reportId").GetGuid();
    }

    private static async Task<HttpResponseMessage> QuyetAsync(
        ModulesTestClient client, Guid reportId, object body, string role = "MODERATOR", Guid? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/reports/{reportId}") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = ModulesTestClient.Bearer(caller ?? Guid.NewGuid(), role);
        return await client.Http.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> KhoiPhucAsync(
        ModulesTestClient client, string path, object? body = null, string role = "MODERATOR", Guid? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = ModulesTestClient.Bearer(caller ?? Guid.NewGuid(), role);
        return await client.Http.SendAsync(request);
    }

    private static async Task<JsonElement> DocAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string?> TypeAsync(HttpResponseMessage response) =>
        (await DocAsync(response)).GetProperty("type").GetString();

    private sealed record DongAudit(Guid Actor, string Action, string? TargetType, string Metadata, string? Ip);

    /// <summary>Mọi dòng audit của một đối tượng (hoặc của một người, khi <paramref name="actor"/> có giá trị).</summary>
    private async Task<List<DongAudit>> AuditAsync(Guid? target = null, Guid? actor = null)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select actor_id, action, target_type, coalesce(metadata::text, ''), host(ip) from moderation.audit_logs " +
            "where ($1::uuid is null or target_id = $1) and ($2::uuid is null or actor_id = $2) order by id", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)target ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)actor ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        var rows = new List<DongAudit>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new DongAudit(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return rows;
    }

    private async Task<List<ContentHidden>> EventCuaAsync(Guid target)
    {
        await factory.DrainEventsAsync();
        return [.. factory.Services.GetRequiredService<Recorded>().Events.Where(e => e.TargetId == target)];
    }

    private static async Task<(string Status, string? Reason)> BaiTrongDbAsync(ModulesTestClient client, Guid post)
    {
        var row = await client.QueryRowAsync("select status, hidden_reason from content.posts where post_id = $1", post);
        return ((string)row!["status"]!, (string?)row["hidden_reason"]);
    }

    private async Task<List<string>> TrangThaiBaoCaoAsync(ModulesTestClient client, Guid target)
    {
        var rows = new List<string>();
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select status from moderation.reports where target_id = $1 order by id", conn);
        cmd.Parameters.AddWithValue(target);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }

    // ---------------------------------------------------------------- AC-01 .. AC-04

    /// <summary>
    /// <c>MOD-01</c> ⭐ (AC-01): ba người báo bài P → Moderator <c>hide</c> MỘT báo cáo → P <c>hidden</c> + lý do; CẢ BA báo cáo
    /// <c>resolved</c>, cùng người, cùng mốc; MỘT dòng audit <c>report.hide</c> có đủ ba <c>reportIds</c>; một <c>ContentHidden</c> đúng vai
    /// (tác giả, không phải Moderator); tác giả đọc bài 200 + <c>moderation</c>, người báo 404; P rời hàng đợi.
    /// </summary>
    [Fact]
    public async Task MOD_01_hide_an_bai_dong_ca_ba_bao_cao_mot_dong_audit_mot_event()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var p = await BaiAsync(client, tacGia);
        var r1 = await BaoCaoAsync(client, p, "spam");
        var r2 = await BaoCaoAsync(client, p, "spam");
        var r3 = await BaoCaoAsync(client, p, "violence");
        var moderator = Guid.NewGuid();

        var ketQua = await OkAsync(await QuyetAsync(client, r2, new { decision = "hide", note = "Quảng cáo lặp lại." }, caller: moderator));

        Assert.Equal("hide", ketQua.GetProperty("decision").GetString());
        Assert.Equal("hidden", ketQua.GetProperty("targetStatus").GetString());
        Assert.Equal(new[] { r1, r2, r3 }.Order(), ketQua.GetProperty("closedReportIds").EnumerateArray().Select(e => e.GetGuid()).Order());

        Assert.Equal(("hidden", (string?)"spam"), await BaiTrongDbAsync(client, p));   // lý do của báo cáo được mở
        var dong = await client.QueryRowAsync(
            "select count(*) as n, count(distinct resolver_id) as nguoi, count(distinct resolved_at) as moc, min(resolver_id::text) as ai " +
            "from moderation.reports where target_id = $1 and status = 'resolved'", p);
        Assert.Equal((3L, 1L, 1L, moderator.ToString()), ((long)dong!["n"]!, (long)dong["nguoi"]!, (long)dong["moc"]!, (string)dong["ai"]!));

        var audit = Assert.Single(await AuditAsync(p));
        Assert.Equal((moderator, "report.hide", "post"), (audit.Actor, audit.Action, audit.TargetType));
        using (var metadata = JsonDocument.Parse(audit.Metadata))
        {
            Assert.Equal(new[] { r1, r2, r3 }.Order(),
                metadata.RootElement.GetProperty("reportIds").EnumerateArray().Select(e => e.GetGuid()).Order());
            Assert.Equal("spam", metadata.RootElement.GetProperty("reasonCode").GetString());
            Assert.Equal("Quảng cáo lặp lại.", metadata.RootElement.GetProperty("note").GetString());
        }

        var su = Assert.Single(await EventCuaAsync(p));
        Assert.Equal(new ContentHidden(ModerationTargetType.Post, p, p, tacGia, "spam"), su);

        using (var cuaTacGia = await client.GetPostAsync(tacGia, p))
            Assert.Equal("hidden", (await OkAsync(cuaTacGia)).GetProperty("moderation").GetProperty("status").GetString());
        using (var cuaNguoiKhac = await client.GetPostAsync(Guid.NewGuid(), p))
            Assert.Equal(HttpStatusCode.NotFound, cuaNguoiKhac.StatusCode);
    }

    /// <summary><c>MOD-02</c> (AC-02): <c>dismiss</c> → báo cáo <c>dismissed</c>, bài vẫn <c>published</c>, audit <c>report.dismiss</c>, không event.</summary>
    [Fact]
    public async Task MOD_02_dismiss_dong_bao_cao_bai_giu_nguyen_khong_event()
    {
        var client = new ModulesTestClient(factory);
        var p = await BaiAsync(client, await NguoiCoHoSoAsync(client));
        var r = await BaoCaoAsync(client, p);

        var ketQua = await OkAsync(await QuyetAsync(client, r, new { decision = "dismiss" }));

        Assert.Equal("published", ketQua.GetProperty("targetStatus").GetString());
        Assert.Equal(["dismissed"], await TrangThaiBaoCaoAsync(client, p));
        Assert.Equal(("published", (string?)null), await BaiTrongDbAsync(client, p));
        Assert.Equal("report.dismiss", Assert.Single(await AuditAsync(p)).Action);
        Assert.Empty(await EventCuaAsync(p));
    }

    /// <summary>
    /// <c>MOD-03</c> (AC-03): USER gọi <c>PATCH</c> → 403 + đúng một <c>access.denied</c> (tầng 2 ở middleware, route template); báo cáo
    /// vẫn mở.
    /// </summary>
    [Fact]
    public async Task MOD_03_user_403_mot_dong_access_denied_bao_cao_van_mo()
    {
        var client = new ModulesTestClient(factory);
        var p = await BaiAsync(client, await NguoiCoHoSoAsync(client));
        var r = await BaoCaoAsync(client, p);
        var user = Guid.NewGuid();

        using var response = await QuyetAsync(client, r, new { decision = "dismiss" }, "USER", user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var denied = Assert.Single(await AuditAsync(actor: user));
        Assert.Equal("access.denied", denied.Action);
        Assert.Contains("api/v1/reports/{reportId}", denied.Metadata);
        Assert.Equal(["open"], await TrangThaiBaoCaoAsync(client, p));
    }

    /// <summary><c>MOD-04</c> (AC-04): quyết lần hai → 409 <c>report-already-decided</c>, không thêm dòng audit.</summary>
    [Fact]
    public async Task MOD_04_quyet_lan_hai_409_report_already_decided()
    {
        var client = new ModulesTestClient(factory);
        var p = await BaiAsync(client, await NguoiCoHoSoAsync(client));
        var r = await BaoCaoAsync(client, p);
        await OkAsync(await QuyetAsync(client, r, new { decision = "dismiss" }));

        using var lanHai = await QuyetAsync(client, r, new { decision = "hide" });

        Assert.Equal(HttpStatusCode.Conflict, lanHai.StatusCode);
        Assert.Equal("urn:socialapp:problem:report-already-decided", await TypeAsync(lanHai));
        Assert.Single(await AuditAsync(p));
        Assert.Equal(("published", (string?)null), await BaiTrongDbAsync(client, p));
    }

    /// <summary>
    /// <c>MOD-05</c>: bảng <c>decision × targetType</c> qua API — <c>resolve</c> báo cáo bài · <c>hide</c> báo cáo tài khoản → 400
    /// <c>errors.decision</c>; <c>resolve</c> thiếu <c>note</c> → 400 <c>errors.note</c>; đối chứng <c>resolve</c> có ghi chú → 200. Không lần
    /// 400 nào đổi gì.
    /// </summary>
    [Fact]
    public async Task MOD_05_sai_cap_quyet_dinh_400_resolve_can_note()
    {
        var client = new ModulesTestClient(factory);
        var p = await BaiAsync(client, await NguoiCoHoSoAsync(client));
        var rBai = await BaoCaoAsync(client, p);
        var nguoi = await NguoiCoHoSoAsync(client);
        var rNguoi = await BaoCaoAsync(client, nguoi, "harassment", "user");

        foreach (var (reportId, body, truong) in new (Guid, object, string)[]
        {
            (rBai, new { decision = "resolve", note = "Đã xử lý." }, "decision"),
            (rNguoi, new { decision = "hide" }, "decision"),
            (rNguoi, new { decision = "resolve" }, "note"),
            (rNguoi, new { decision = "resolve", note = "   " }, "note"),
            (rBai, new { decision = "ban" }, "decision"),
            (rBai, new { decision = "hide", reasonCode = "abuse" }, "reasonCode"),
        })
        {
            using var response = await QuyetAsync(client, reportId, body);
            var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
            Assert.Equal(400, status);
            Assert.True(errors.ContainsKey(truong), $"Thiếu errors.{truong}: {string.Join(", ", errors.Keys)}");
        }

        Assert.Empty(await AuditAsync(p));
        Assert.Empty(await AuditAsync(nguoi));

        var ketQua = await OkAsync(await QuyetAsync(client, rNguoi, new { decision = "resolve", note = "Admin đã khóa tài khoản." }));
        Assert.Equal("active", ketQua.GetProperty("targetStatus").GetString());
        Assert.Equal("report.resolve", Assert.Single(await AuditAsync(nguoi)).Action);
    }

    /// <summary>
    /// <c>MOD-06</c>: khôi phục bài bị ẩn → 200, bài <c>published</c>, <c>hidden_reason</c> null, audit <c>content.restore</c> có ghi chú;
    /// báo cáo đã đóng GIỮ NGUYÊN, không event. Khôi phục lần hai (bài đang <c>published</c>) → 409 <c>moderation-not-hidden</c>.
    /// </summary>
    [Fact]
    public async Task MOD_06_khoi_phuc_bai_an_200_lan_hai_409()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);
        var p = await BaiAsync(client, tacGia);
        var r = await BaoCaoAsync(client, p);
        await OkAsync(await QuyetAsync(client, r, new { decision = "hide" }));
        var path = $"/api/v1/moderation/targets/post/{p}/restore";
        var nguoiKhoiPhuc = Guid.NewGuid();

        var ketQua = await OkAsync(await KhoiPhucAsync(client, path, new { note = "Ẩn nhầm." }, caller: nguoiKhoiPhuc));

        Assert.Equal(("post", p, "published"), (ketQua.GetProperty("targetType").GetString(),
            ketQua.GetProperty("targetId").GetGuid(), ketQua.GetProperty("targetStatus").GetString()));
        Assert.Equal(("published", (string?)null), await BaiTrongDbAsync(client, p));
        Assert.Equal(["resolved"], await TrangThaiBaoCaoAsync(client, p));
        var restore = (await AuditAsync(p)).Last();
        Assert.Equal((nguoiKhoiPhuc, "content.restore"), (restore.Actor, restore.Action));
        Assert.Contains("Ẩn nhầm.", restore.Metadata);
        using (var cuaNguoiKhac = await client.GetPostAsync(Guid.NewGuid(), p))
            Assert.Equal(HttpStatusCode.OK, cuaNguoiKhac.StatusCode);
        Assert.Single(await EventCuaAsync(p));   // chỉ ContentHidden của lần ẩn — khôi phục không phát gì

        using var lanHai = await KhoiPhucAsync(client, path);   // không body: được
        Assert.Equal(HttpStatusCode.Conflict, lanHai.StatusCode);
        Assert.Equal("urn:socialapp:problem:moderation-not-hidden", await TypeAsync(lanHai));
    }

    [Theory]
    [InlineData("user", "0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20", 400)]
    [InlineData("video", "0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20", 400)]
    [InlineData("post", "abc", 400)]
    [InlineData("post", "0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20", 404)]
    [InlineData("comment", "0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20", 404)]   // bình luận không tồn tại (có provider từ bước 9)
    public async Task Khoi_phuc_tham_so_sai_400_khong_ton_tai_404(string targetType, string targetId, int expected)
    {
        var client = new ModulesTestClient(factory);
        using var response = await KhoiPhucAsync(client, $"/api/v1/moderation/targets/{targetType}/{targetId}/restore");
        Assert.Equal(expected, (int)response.StatusCode);
    }

    /// <summary>
    /// <c>MOD-07</c> <i>(đề xuất)</i>: báo cáo thứ hai cho bài ĐÃ ẩn (đặt bằng SQL — API không cho báo bài bị ẩn) → <c>hide</c> vẫn 200,
    /// báo cáo đóng, KHÔNG có <c>ContentHidden</c> thứ hai — tác giả đã được báo ở lần đầu.
    /// </summary>
    [Fact]
    public async Task MOD_07_hide_bao_cao_thu_hai_cua_bai_da_an_khong_phat_lai()
    {
        var client = new ModulesTestClient(factory);
        var p = await BaiAsync(client, await NguoiCoHoSoAsync(client));
        await OkAsync(await QuyetAsync(client, await BaoCaoAsync(client, p), new { decision = "hide" }));
        var r2 = Uuid7.New();
        await client.ExecuteSqlAsync(
            "insert into moderation.reports (id, reporter_id, target_type, target_id, reason_code, status, created_at, updated_at) " +
            "values ($1, $2, 'post', $3, 'nudity', 'open', now(), now())", r2, Guid.NewGuid(), p);

        var ketQua = await OkAsync(await QuyetAsync(client, r2, new { decision = "hide" }));

        Assert.Equal([r2], ketQua.GetProperty("closedReportIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal(("hidden", (string?)"spam"), await BaiTrongDbAsync(client, p));   // lý do của lần ẩn đầu, không ghi đè
        Assert.Single(await EventCuaAsync(p));
    }

    // ---------------------------------------------------------------- tầng 2 kép, audit

    /// <summary>
    /// <c>ROLE-01</c> vế <c>hide</c> (Đ-6.9, L-D12): vai trò tự tạo chỉ có <c>report.resolve</c> đọc hàng đợi 200, <c>dismiss</c> 200,
    /// <c>hide</c> → 403 + MỘT <c>access.denied</c> target là báo cáo, <c>metadata.permission = post.hide</c>; bài vẫn <c>published</c>, báo cáo
    /// vẫn mở. <c>hide</c> trên id báo cáo KHÔNG tồn tại cũng 403, không 404 — tầng 2 kép đứng trước mọi I/O.
    /// </summary>
    [Fact]
    public async Task ROLE_01_hide_reviewer_chi_co_report_resolve_403_co_access_denied()
    {
        var client = new ModulesTestClient(factory);
        var reviewer = IdentitySql.MaVaiTroMoi("REVIEWER");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, reviewer, "report.resolve");
        var caller = Guid.NewGuid();
        var tacGia = await NguoiCoHoSoAsync(client);
        var (p, q) = (await BaiAsync(client, tacGia), await BaiAsync(client, tacGia));
        var (rp, rq) = (await BaoCaoAsync(client, p), await BaoCaoAsync(client, q));

        using (var hangDoi = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports"))
        {
            hangDoi.Headers.Authorization = ModulesTestClient.Bearer(caller, reviewer);
            using var response = await client.Http.SendAsync(hangDoi);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await OkAsync(await QuyetAsync(client, rq, new { decision = "dismiss" }, reviewer, caller));

        using var hide = await QuyetAsync(client, rp, new { decision = "hide" }, reviewer, caller);
        Assert.Equal(HttpStatusCode.Forbidden, hide.StatusCode);
        using var hideIdLa = await QuyetAsync(client, Guid.NewGuid(), new { decision = "hide" }, reviewer, caller);
        Assert.Equal(HttpStatusCode.Forbidden, hideIdLa.StatusCode);

        var denied = (await AuditAsync(rp)).Where(a => a.Action == "access.denied").ToList();
        var dong = Assert.Single(denied);
        Assert.Equal((caller, "report"), (dong.Actor, dong.TargetType));
        using (var metadata = JsonDocument.Parse(dong.Metadata))
            Assert.Equal("post.hide", metadata.RootElement.GetProperty("permission").GetString());
        Assert.Equal(("published", (string?)null), await BaiTrongDbAsync(client, p));
        Assert.Equal(["open"], await TrangThaiBaoCaoAsync(client, p));
    }

    /// <summary>
    /// <c>AUD-01</c>: bài có <c>SECRET-xyz</c> trong nội dung, báo cáo có <c>SECRET-abc</c> trong mô tả → <c>hide</c> rồi khôi phục: mọi
    /// dòng audit có <c>actor_id</c>, <c>action</c>, <c>ip</c>, và KHÔNG dòng nào chứa <c>SECRET-</c> — audit ghi id và mã, không nội dung.
    /// </summary>
    [Fact]
    public async Task AUD_01_audit_khong_chua_noi_dung_nguoi_dung()
    {
        var client = new ModulesTestClient(factory);
        var p = await BaiAsync(client, await NguoiCoHoSoAsync(client), "Nội dung có SECRET-xyz bên trong.");
        var r = await BaoCaoAsync(client, p, "other", detail: "Mô tả có SECRET-abc.");

        await OkAsync(await QuyetAsync(client, r, new { decision = "hide", note = "Vi phạm." }));
        await OkAsync(await KhoiPhucAsync(client, $"/api/v1/moderation/targets/post/{p}/restore", new { note = "Xem lại." }));

        var rows = await AuditAsync(p);
        Assert.Equal(["report.hide", "content.restore"], rows.Select(a => a.Action));
        Assert.All(rows, a =>
        {
            Assert.NotEqual(Guid.Empty, a.Actor);
            Assert.False(string.IsNullOrEmpty(a.Ip));
            Assert.DoesNotContain("SECRET-", a.Metadata);
        });
    }

    // ---------------------------------------------------------------- đồng thời

    /// <summary>
    /// <c>MOD-C1</c> ⭐: hai Moderator quyết CÙNG một báo cáo cùng lúc (một <c>hide</c>, một <c>dismiss</c>) — đúng một 200, một 409
    /// <c>report-already-decided</c>, đúng MỘT dòng audit quyết định, không 500. Hai mươi lượt, mỗi lượt một bài mới.
    /// </summary>
    [Fact]
    public async Task MOD_C1_hai_moderator_quyet_cung_luc_mot_200_mot_409_mot_dong_audit_20_luot()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = await NguoiCoHoSoAsync(client);

        for (var luot = 0; luot < 20; luot++)
        {
            var p = await BaiAsync(client, tacGia);
            var r = await BaoCaoAsync(client, p);

            var responses = await Task.WhenAll(
                QuyetAsync(client, r, new { decision = "hide" }),
                QuyetAsync(client, r, new { decision = "dismiss" }));

            var codes = responses.Select(x => (int)x.StatusCode).Order().ToList();
            Assert.True(codes.SequenceEqual([200, 409]), $"Lượt {luot}: {string.Join(", ", codes)}");
            var thua = responses.Single(x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("urn:socialapp:problem:report-already-decided", await TypeAsync(thua));
            Assert.Single(await AuditAsync(p));
            foreach (var x in responses)
                x.Dispose();
        }
    }

    private sealed class Recorded
    {
        public ConcurrentQueue<ContentHidden> Events { get; } = new();
    }

    private sealed class RecordingHandler(Recorded recorded) : IIntegrationEventHandler<ContentHidden>
    {
        public Task HandleAsync(ContentHidden integrationEvent, CancellationToken ct)
        {
            recorded.Events.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
