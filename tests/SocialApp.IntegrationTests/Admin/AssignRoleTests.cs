using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Admin;

/// <summary>
/// GĐ6 D4 — <c>PUT /admin/users/{userId}/role</c> (Mục 7.3, Đ-6.6, Đ-6.7, L-D18) trên Postgres + Redis thật. Mốc 1 của GĐ6: đổi vai
/// trò có hiệu lực ở request kế tiếp mà người đó KHÔNG bị đăng xuất — chứng minh bằng đăng nhập thật và refresh cùng family.
///
/// Admin của lớp (<see cref="_admin"/>) là tài khoản ADMIN có thật trong DB: seed không tạo Admin nào, không có Admin hoạt động thì
/// bất biến Đ-6.7 chặn mọi lần hạ quyền.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AssignRoleTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private AdminTestClient _client = null!;
    private Guid _admin;

    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);

        _client = new AdminTestClient(factory);
        _admin = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> AssignAsync(Guid target, string roleCode, Guid? caller = null, string role = "ADMIN") =>
        _client.AssignRoleAsync(target, new { roleCode }, caller ?? _admin, role);

    private static string[] Permissions(JsonElement me) =>
        [.. me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];

    /// <summary>
    /// ADM-05 ⭐: hạ X từ MODERATOR xuống USER → 200 <c>applied</c>; access cũ 401; refresh CÙNG cookie → 200 và token mới
    /// <c>role = USER</c> (X không mất phiên); <c>/me</c> → <c>USER</c>, không còn <c>report.resolve</c>; family KHÔNG bị thu hồi;
    /// audit <c>role.assign { fromRole: MODERATOR, toRole: USER }</c>.
    /// </summary>
    [Fact]
    public async Task ADM_05_ha_MODERATOR_xuong_USER_token_cu_401_refresh_200_mang_vai_tro_moi()
    {
        var (x, email) = await _client.TaiKhoanDangNhapDuocAsync("MODERATOR", "adm05");
        var session = await _client.DangNhapAsync(email);
        Assert.Equal("MODERATOR", AdminTestClient.ClaimOf(session.Access, "role"));
        await AdminTestClient.QuaGiayCuaAsync(session.Access);

        using var response = await AssignAsync(x, "USER");

        var body = await AdminTestClient.OkBodyAsync(response);
        Assert.Equal("applied", body.GetProperty("revocation").GetString());
        Assert.Equal("USER", body.GetProperty("user").GetProperty("roleCode").GetString());
        Assert.Equal("Người dùng", body.GetProperty("user").GetProperty("roleDisplayName").GetString());

        using (var stale = await _client.MeAsync(session.Access))
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        using var refreshed = await _client.RefreshAsync(session.Refresh);
        var tokens = await AdminTestClient.OkBodyAsync(refreshed);
        var access = tokens.GetProperty("accessToken").GetString()!;
        Assert.Equal("USER", AdminTestClient.ClaimOf(access, "role"));

        using var me = await _client.MeAsync(access);
        var meBody = await AdminTestClient.OkBodyAsync(me);
        Assert.Equal("USER", meBody.GetProperty("role").GetString());
        Assert.DoesNotContain("report.resolve", Permissions(meBody));
        Assert.DoesNotContain("post.hide", Permissions(meBody));

        Assert.Equal(1, await _client.AuditCountAsync(x, "role.assign"));
        Assert.Equal("MODERATOR|USER", await _client.ScalarAsync<string>(
            "SELECT (metadata->>'fromRole') || '|' || (metadata->>'toRole') FROM moderation.audit_logs WHERE target_id = $1", x));
    }

    /// <summary>ADM-05b (L-D10): gán đúng vai trò đang có → 200 <c>not-needed</c>; không audit; không key <c>revoked:user</c>.</summary>
    [Fact]
    public async Task ADM_05b_gan_dung_vai_tro_dang_co_200_not_needed_khong_audit_khong_Redis()
    {
        var (x, _) = await _client.TaiKhoanDangNhapDuocAsync("MODERATOR", "adm05b");

        using var response = await AssignAsync(x, "MODERATOR");

        Assert.Equal("not-needed", (await AdminTestClient.OkBodyAsync(response)).GetProperty("revocation").GetString());
        Assert.Equal(0, await _client.AuditCountAsync(x, "role.assign"));
        Assert.False(await redis.Database.KeyExistsAsync($"revoked:user:{x}"));
    }

    /// <summary>
    /// ADM-04 (vế hạ quyền): Admin hoạt động CUỐI CÙNG tự hạ mình → 409 <c>last-admin</c>; vai trò không đổi; không key
    /// <c>revoked:user</c> (Redis chỉ ghi sau COMMIT); 0 dòng audit. Tự hạ được — chặn là do bất biến, không do "tự".
    /// </summary>
    [Fact]
    public async Task ADM_04_ha_Admin_hoat_dong_cuoi_cung_409_khong_doi_gi()
    {
        var last = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);
        await _client.DisableOtherAdminsAsync(except: last);

        try
        {
            using var response = await AssignAsync(last, "USER", caller: last);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("urn:socialapp:problem:last-admin", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("ADMIN", await _client.RoleCodeAsync(last));
            Assert.False(await redis.Database.KeyExistsAsync($"revoked:user:{last}"));
            Assert.Equal(0, await _client.AuditCountAsync(last, "role.assign"));
        }
        finally
        {
            // Trả Admin của lớp về hoạt động — ca khác trong lớp gọi dưới quyền của nó.
            await _client.ExecAsync("UPDATE identity.users SET status = 'active' WHERE user_id = $1", _admin);
        }
    }

    /// <summary>Tự hạ vai trò của chính mình ĐƯỢC khi còn Admin khác (Đ-6.7) — khác tự khóa (400).</summary>
    [Fact]
    public async Task Tu_ha_vai_tro_khi_con_Admin_khac_200()
    {
        var self = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);

        using var response = await AssignAsync(self, "MODERATOR", caller: self);

        Assert.Equal("applied", (await AdminTestClient.OkBodyAsync(response)).GetProperty("revocation").GetString());
        Assert.Equal("MODERATOR", await _client.RoleCodeAsync(self));
    }

    /// <summary>
    /// ROLE-01 (vế gán): vai trò tự tạo (SQL — API vai trò là D5) có <c>report.resolve</c> gán cho R đang đăng nhập → refresh cùng
    /// family ra token <c>role</c> = mã vai trò mới; <c>/me.permissions</c> đúng tập của vai trò đó. Vế <c>hide</c> là D7c.
    /// </summary>
    [Fact]
    public async Task ROLE_01_gan_vai_tro_tu_tao_refresh_ra_token_mang_ma_moi_va_dung_tap_quyen()
    {
        var reviewer = IdentitySql.MaVaiTroMoi("REVIEWER");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, reviewer, "report.resolve");
        var (r, email) = await _client.TaiKhoanDangNhapDuocAsync(tag: "role01");
        var session = await _client.DangNhapAsync(email);
        await AdminTestClient.QuaGiayCuaAsync(session.Access);

        using var response = await AssignAsync(r, reviewer);
        Assert.Equal(reviewer, (await AdminTestClient.OkBodyAsync(response)).GetProperty("user").GetProperty("roleCode").GetString());

        using var refreshed = await _client.RefreshAsync(session.Refresh);
        var access = (await AdminTestClient.OkBodyAsync(refreshed)).GetProperty("accessToken").GetString()!;
        Assert.Equal(reviewer, AdminTestClient.ClaimOf(access, "role"));

        using var me = await _client.MeAsync(access);
        Assert.Equal(["report.resolve"], Permissions(await AdminTestClient.OkBodyAsync(me)));
    }

    /// <summary>
    /// L-D18 ⭐: vai trò "Nhân sự" chỉ có <c>role.assign</c> KHÔNG tự nâng mình lên ADMIN — 403, vai trò không đổi, một dòng
    /// <c>access.denied</c> mang <c>permission = role.manage</c>. Không có tầng 2 kép này, <c>role.assign</c> tương đương toàn quyền.
    /// </summary>
    [Fact]
    public async Task L_D18_chi_co_role_assign_khong_tu_nang_len_ADMIN_403_co_audit()
    {
        var hr = IdentitySql.MaVaiTroMoi("HR");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, hr, "role.assign");
        var (me, _) = await _client.TaiKhoanDangNhapDuocAsync(hr, "hr");

        using var response = await AssignAsync(me, "ADMIN", caller: me, role: hr);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(hr, await _client.RoleCodeAsync(me));
        Assert.Equal("role.manage", await _client.ScalarAsync<string>(
            "SELECT metadata->>'permission' FROM moderation.audit_logs WHERE action = 'access.denied' AND target_id = $1", me));
        Assert.Equal(0, await _client.AuditCountAsync(me, "role.assign"));
    }

    /// <summary>L-D18 vế 2: "Nhân sự" không hạ được một ADMIN (người bị đổi đang là ADMIN — chỉ biết sau khi khóa dòng).</summary>
    [Fact]
    public async Task L_D18_chi_co_role_assign_khong_ha_duoc_ADMIN_403()
    {
        var hr = IdentitySql.MaVaiTroMoi("HR");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, hr, "role.assign");
        var victim = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);

        using var response = await AssignAsync(victim, "USER", caller: Guid.NewGuid(), role: hr);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ADMIN", await _client.RoleCodeAsync(victim));
        Assert.False(await redis.Database.KeyExistsAsync($"revoked:user:{victim}"));
        Assert.Equal(1, await _client.AuditCountAsync(victim, "access.denied"));
    }

    /// <summary>Đối chứng L-D18: "Nhân sự" gán vai trò KHÔNG chạm ADMIN được (200); có thêm <c>role.manage</c> thì nâng lên ADMIN được.</summary>
    [Fact]
    public async Task L_D18_doi_chung_gan_ngoai_ADMIN_200_co_role_manage_nang_ADMIN_200()
    {
        var hr = IdentitySql.MaVaiTroMoi("HR");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, hr, "role.assign");
        var hrManager = IdentitySql.MaVaiTroMoi("HRM");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, hrManager, "role.assign", "role.manage");
        var (u, _) = await _client.TaiKhoanDangNhapDuocAsync(tag: "ld18");

        using (var moderator = await AssignAsync(u, "MODERATOR", caller: Guid.NewGuid(), role: hr))
            Assert.Equal(HttpStatusCode.OK, moderator.StatusCode);
        using (var admin = await AssignAsync(u, "ADMIN", caller: Guid.NewGuid(), role: hrManager))
            Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("ADMIN", await _client.RoleCodeAsync(u));
    }

    /// <summary>
    /// <c>roleCode</c> so CHÍNH XÁC (cạm bẫy 2): <c>user</c> không phải <c>USER</c>; mã không tồn tại; thiếu; quá 30 ký tự — đều 400
    /// <c>errors.roleCode</c>, vai trò không đổi.
    /// </summary>
    [Theory]
    [InlineData("{\"roleCode\":\"user\"}")]
    [InlineData("{\"roleCode\":\"KHONG_CO_VAI_TRO\"}")]
    [InlineData("{}")]
    [InlineData("{\"roleCode\":\"\"}")]
    [InlineData("{\"roleCode\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    public async Task Vai_tro_sai_400_errors_roleCode(string json)
    {
        var (x, _) = await _client.TaiKhoanDangNhapDuocAsync(tag: "bad");

        using var http = _client.Http();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/users/{x}/role")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(_admin, "ADMIN");
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["roleCode"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal("USER", await _client.RoleCodeAsync(x));
    }

    [Fact]
    public async Task Dich_khong_ton_tai_404()
    {
        using var response = await AssignAsync(Guid.NewGuid(), "USER");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
