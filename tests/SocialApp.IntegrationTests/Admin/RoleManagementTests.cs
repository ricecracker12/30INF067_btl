using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Admin;

/// <summary>
/// GĐ6 D5 — CRUD vai trò + <c>GET /admin/permissions</c> (Đ-6.9, Đ-6.10, L-D11, L-D19) trên Postgres + Redis thật. "Nâng cấp là thay
/// dữ liệu, không thay code" (PTTK 6.7.2): tạo vai trò qua API rồi gán là dùng được, không sửa dòng nghiệp vụ nào.
///
/// Admin gọi bằng token <c>TestJwt</c> vai trò <c>ADMIN</c> — tầng 2 <c>role.manage</c> đi short-circuit, không cần dòng DB. Ca sửa
/// quyền USER/MODERATOR trả tập về nguyên trong <c>finally</c>: database là của cả lớp.
/// </summary>
[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class RoleManagementTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private const short UserRoleId = 1;
    private const short ModeratorRoleId = 2;
    private const short AdminRoleId = 3;

    private static readonly Guid Admin = Guid.NewGuid();

    private AdminTestClient _client = null!;

    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
        _client = new AdminTestClient(factory);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    // --- Gọi API --------------------------------------------------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string role = "ADMIN")
    {
        using var http = _client.Http();
        using var request = new HttpRequestMessage(method, path);
        if (body is string raw)
            request.Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json");
        else if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = ModulesTestClient.Bearer(Admin, role);
        return await http.SendAsync(request);
    }

    private async Task<JsonElement> CreateRoleOkAsync(string code, params string[] permissions)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/roles",
            new { code, displayName = $"Vai trò {code}", permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private Task<HttpResponseMessage> SetPermissionsAsync(short roleId, string[] permissions, bool? confirm = null) =>
        SendAsync(HttpMethod.Put, $"/api/v1/admin/roles/{roleId}/permissions",
            confirm is null ? new { permissions } : new { permissions, confirm });

    private async Task<string[]> GrantsAsync(short roleId)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT p.code FROM identity.role_permissions rp JOIN identity.permissions p ON p.permission_id = rp.permission_id
             WHERE rp.role_id = $1 ORDER BY p.permission_id
            """, conn);
        cmd.Parameters.AddWithValue(roleId);
        var codes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            codes.Add(reader.GetString(0));
        return [.. codes];
    }

    private Task<long> RoleAuditCountAsync(string action, string code) => _client.ScalarAsync<long>(
        "SELECT count(*) FROM moderation.audit_logs WHERE action = $1 AND target_type = 'role' AND metadata->>'code' = $2",
        action, code);

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"mong {(int)status}, nhận {(int)response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    // --- Đọc --------------------------------------------------------------------------------------------------------------------

    /// <summary>ADMIN: đủ 18 mã, <c>editable: false</c>; USER: đúng tập DB; <c>isSystem</c> tính từ mã; <c>userCount</c> mọi trạng thái.</summary>
    [Fact]
    public async Task Danh_sach_vai_tro_quyen_hieu_luc_va_so_nguoi_mang()
    {
        await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, $"mod-{Guid.NewGuid():N}@test.local", "MODERATOR", "disabled");
        var custom = await CreateRoleOkAsync(IdentitySql.MaVaiTroMoi("LIST"), "report.resolve");

        using var response = await SendAsync(HttpMethod.Get, "/api/v1/admin/roles");
        var roles = (await AdminTestClient.OkBodyAsync(response)).EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);

        var admin = roles["ADMIN"];
        Assert.Equal(18, admin.GetProperty("permissions").GetArrayLength());
        Assert.False(admin.GetProperty("editable").GetBoolean());
        Assert.True(admin.GetProperty("isSystem").GetBoolean());
        Assert.Equal(await GrantsAsync(UserRoleId), Strings(roles["USER"].GetProperty("permissions")));
        Assert.True(roles["MODERATOR"].GetProperty("userCount").GetInt32() >= 1);   // tài khoản disabled vẫn tính
        var mine = roles[custom.GetProperty("code").GetString()!];
        Assert.False(mine.GetProperty("isSystem").GetBoolean());
        Assert.True(mine.GetProperty("editable").GetBoolean());
        Assert.True(mine.GetProperty("roleId").GetInt32() >= 100);
        Assert.Equal(["report.resolve"], Strings(mine.GetProperty("permissions")));
    }

    /// <summary>18 mã theo <c>permission_id</c>; chỉ <c>role.manage</c> có <c>assignable: false</c> (L-D19).</summary>
    [Fact]
    public async Task Danh_muc_quyen_18_ma_role_manage_khong_gan_duoc()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/admin/permissions");
        var items = (await AdminTestClient.OkBodyAsync(response)).EnumerateArray().ToList();

        Assert.Equal(18, items.Count);
        Assert.Equal("post.read.public", items[0].GetProperty("code").GetString());
        Assert.Equal("role.manage", items[^1].GetProperty("code").GetString());
        Assert.Equal(["role.manage"],
            items.Where(i => !i.GetProperty("assignable").GetBoolean()).Select(i => i.GetProperty("code").GetString()));
    }

    // --- Tạo, đổi tên, xóa ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ROLE-01 ⭐ (tạo + gán): <c>POST /admin/roles { REVIEWER, [report.resolve] }</c> → gán cho R (D4) → R refresh → <c>/me</c> đúng
    /// <c>report.resolve</c>. Vế <c>GET /reports</c> 200 / <c>hide</c> 403 là D7c. Audit <c>role.create</c> mang <c>code</c>, <c>added</c>.
    /// </summary>
    [Fact]
    public async Task ROLE_01_tao_vai_tro_qua_API_gan_cho_R_R_co_dung_quyen_khong_sua_code()
    {
        var code = IdentitySql.MaVaiTroMoi("REVIEWER");
        var created = await CreateRoleOkAsync(code, "report.resolve", "report.resolve");   // trùng trong request được gộp
        Assert.Equal(["report.resolve"], Strings(created.GetProperty("permissions")));
        Assert.Equal(0, created.GetProperty("userCount").GetInt32());

        var adminInDb = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);
        var (r, email) = await _client.TaiKhoanDangNhapDuocAsync(tag: "role01api");
        var session = await _client.DangNhapAsync(email);
        await AdminTestClient.QuaGiayCuaAsync(session.Access);

        using (var assigned = await _client.AssignRoleAsync(r, new { roleCode = code }, adminInDb))
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        using var refreshed = await _client.RefreshAsync(session.Refresh);
        var access = (await AdminTestClient.OkBodyAsync(refreshed)).GetProperty("accessToken").GetString()!;
        using var me = await _client.MeAsync(access);
        var body = await AdminTestClient.OkBodyAsync(me);
        Assert.Equal(code, body.GetProperty("role").GetString());
        Assert.Equal(["report.resolve"], Strings(body.GetProperty("permissions")));

        Assert.Equal(1, await RoleAuditCountAsync("role.create", code));
        Assert.Equal("[\"report.resolve\"]", await _client.ScalarAsync<string>(
            "SELECT (metadata->'added')::text FROM moderation.audit_logs WHERE action = 'role.create' AND metadata->>'code' = $1", code));
    }

    /// <summary>Mã trùng → 409 <c>role-code-taken</c> (bắt unique violation); mã hệ thống → 400; mã quyền lạ hay <c>role.manage</c> → 400.</summary>
    [Fact]
    public async Task Tao_vai_tro_ma_trung_409_ma_he_thong_va_quyen_khong_gan_duoc_400()
    {
        var code = IdentitySql.MaVaiTroMoi("DUP");
        await CreateRoleOkAsync(code);

        using (var dup = await SendAsync(HttpMethod.Post, "/api/v1/admin/roles", new { code, displayName = "x", permissions = Array.Empty<string>() }))
        {
            var problem = await ProblemAsync(dup, HttpStatusCode.Conflict);
            Assert.Equal("urn:socialapp:problem:role-code-taken", problem.GetProperty("type").GetString());
        }

        foreach (var (body, field) in new (object Body, string Field)[]
                 {
                     (new { code = "MODERATOR", displayName = "x", permissions = Array.Empty<string>() }, "code"),
                     (new { code = IdentitySql.MaVaiTroMoi("X"), displayName = "x", permissions = new[] { "role.manage" } }, "permissions"),
                     (new { code = IdentitySql.MaVaiTroMoi("X"), displayName = "x", permissions = new[] { "khong.co" } }, "permissions"),
                     (new { code = "thuong", displayName = "x", permissions = Array.Empty<string>() }, "code"),
                 })
        {
            using var bad = await SendAsync(HttpMethod.Post, "/api/v1/admin/roles", body);
            var problem = await ProblemAsync(bad, HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
        }
    }

    /// <summary>
    /// ROLE-02: <c>PATCH</c> có <c>code</c> · có trường lạ → 400 theo tên trường, thân lỗi không lộ <c>SocialApp.</c>; <c>code</c> không
    /// đổi. Đổi tên (kể cả vai trò hệ thống — trigger chỉ chặn <c>code</c>) → 200 + audit; cùng tên → 200, không audit.
    /// </summary>
    [Fact]
    public async Task ROLE_02_doi_ten_chi_nhan_displayName_co_code_hay_truong_la_400()
    {
        var code = IdentitySql.MaVaiTroMoi("REN");
        var roleId = (short)(await CreateRoleOkAsync(code)).GetProperty("roleId").GetInt32();

        foreach (var (raw, field) in new[]
                 {
                     ($$"""{"displayName":"Tên mới","code":"HACKED"}""", "code"),
                     ($$"""{"displayName":"Tên mới","extra":1}""", "extra"),
                 })
        {
            using var bad = await SendAsync(HttpMethod.Patch, $"/api/v1/admin/roles/{roleId}", raw);
            var problem = await ProblemAsync(bad, HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
            Assert.DoesNotContain("SocialApp.", problem.GetRawText(), StringComparison.Ordinal);
        }
        Assert.Equal(code, await _client.ScalarAsync<string>("SELECT code FROM identity.roles WHERE role_id = $1", roleId));

        using (var renamed = await SendAsync(HttpMethod.Patch, $"/api/v1/admin/roles/{roleId}", new { displayName = "  Tên mới  " }))
            Assert.Equal("Tên mới", (await AdminTestClient.OkBodyAsync(renamed)).GetProperty("displayName").GetString());
        using (var same = await SendAsync(HttpMethod.Patch, $"/api/v1/admin/roles/{roleId}", new { displayName = "Tên mới" }))
            Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(1, await RoleAuditCountAsync("role.rename", code));

        var original = await _client.ScalarAsync<string>("SELECT display_name FROM identity.roles WHERE role_id = $1", ModeratorRoleId);
        try
        {
            using var system = await SendAsync(HttpMethod.Patch, $"/api/v1/admin/roles/{ModeratorRoleId}", new { displayName = "Điều phối viên" });
            Assert.Equal("Điều phối viên", (await AdminTestClient.OkBodyAsync(system)).GetProperty("displayName").GetString());
        }
        finally
        {
            await _client.ExecAsync("UPDATE identity.roles SET display_name = $1 WHERE role_id = $2", original!, ModeratorRoleId);
        }
    }

    /// <summary>
    /// ROLE-03: xóa ADMIN/USER/MODERATOR → 409 <c>system-role</c> · vai trò còn người mang (kể cả bị khóa) → 409 <c>role-in-use</c>
    /// · vai trò tự tạo trống → 204, tập quyền đi theo, audit <c>role.delete</c> · lần hai → 404.
    /// </summary>
    [Fact]
    public async Task ROLE_03_xoa_vai_tro_he_thong_409_con_nguoi_409_trong_204()
    {
        foreach (var system in new[] { UserRoleId, ModeratorRoleId, AdminRoleId })
        {
            using var response = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/roles/{system}");
            var problem = await ProblemAsync(response, HttpStatusCode.Conflict);
            Assert.Equal("urn:socialapp:problem:system-role", problem.GetProperty("type").GetString());
        }

        var usedCode = IdentitySql.MaVaiTroMoi("USED");
        var used = (short)(await CreateRoleOkAsync(usedCode, "report.create")).GetProperty("roleId").GetInt32();
        await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, $"used-{Guid.NewGuid():N}@test.local", usedCode, "disabled");
        using (var inUse = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/roles/{used}"))
        {
            var problem = await ProblemAsync(inUse, HttpStatusCode.Conflict);
            Assert.Equal("urn:socialapp:problem:role-in-use", problem.GetProperty("type").GetString());
        }

        var emptyCode = IdentitySql.MaVaiTroMoi("EMPTY");
        var empty = (short)(await CreateRoleOkAsync(emptyCode, "report.create")).GetProperty("roleId").GetInt32();
        using (var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/roles/{empty}"))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await GrantsAsync(empty));
        Assert.Equal(0L, await _client.ScalarAsync<long>("SELECT count(*) FROM identity.roles WHERE role_id = $1", empty));
        Assert.Equal(1, await RoleAuditCountAsync("role.delete", emptyCode));

        using var again = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/roles/{empty}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    // --- Sửa tập quyền ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ROLE-04 ⭐: sửa quyền USER KHÔNG <c>confirm</c> → 409 <c>confirmation-required</c> mang <c>added</c>, <c>removed</c>,
    /// <c>affectedUsers</c> đúng (và <c>traceId</c>), DB KHÔNG đổi · có <c>confirm</c> → 200, audit <c>confirmed: true</c> · về rỗng → 400.
    /// </summary>
    [Fact]
    public async Task ROLE_04_sua_quyen_USER_can_xac_nhan_o_server_va_khong_ve_rong()
    {
        var before = await GrantsAsync(UserRoleId);
        var without = before.Where(c => c != "post.create").ToArray();
        await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, $"u-{Guid.NewGuid():N}@test.local", status: "disabled");
        var users = await _client.ScalarAsync<long>("SELECT count(*) FROM identity.users WHERE role_id = $1", UserRoleId);

        try
        {
            using (var unconfirmed = await SetPermissionsAsync(UserRoleId, without))
            {
                var problem = await ProblemAsync(unconfirmed, HttpStatusCode.Conflict);
                Assert.Equal("urn:socialapp:problem:confirmation-required", problem.GetProperty("type").GetString());
                Assert.Empty(Strings(problem.GetProperty("added")));
                Assert.Equal(["post.create"], Strings(problem.GetProperty("removed")));
                Assert.Equal(users, problem.GetProperty("affectedUsers").GetInt64());
                Assert.Equal(unconfirmed.Headers.GetValues("X-Correlation-ID").Single(), problem.GetProperty("traceId").GetString());
            }
            Assert.Equal(before, await GrantsAsync(UserRoleId));   // 409 trả TRƯỚC mọi ghi

            using (var falseConfirm = await SetPermissionsAsync(UserRoleId, without, confirm: false))
                Assert.Equal(HttpStatusCode.Conflict, falseConfirm.StatusCode);

            using (var empty = await SetPermissionsAsync(UserRoleId, [], confirm: true))
            {
                var problem = await ProblemAsync(empty, HttpStatusCode.BadRequest);
                Assert.True(problem.GetProperty("errors").TryGetProperty("permissions", out _));
            }

            using (var confirmed = await SetPermissionsAsync(UserRoleId, without, confirm: true))
                Assert.Equal(without, Strings((await AdminTestClient.OkBodyAsync(confirmed)).GetProperty("permissions")));
            Assert.Equal(without, await GrantsAsync(UserRoleId));
            Assert.Equal("true|[\"post.create\"]", await _client.ScalarAsync<string>("""
                SELECT (metadata->>'confirmed') || '|' || (metadata->'removed')::text FROM moderation.audit_logs
                 WHERE action = 'role.permissions' AND metadata->>'code' = 'USER' ORDER BY id DESC LIMIT 1
                """));
        }
        finally
        {
            using var restore = await SetPermissionsAsync(UserRoleId, before, confirm: true);
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }
    }

    /// <summary>Không đổi gì → 200, không audit (L-D10). Vai trò tự tạo không cần xác nhận và về rỗng được.</summary>
    [Fact]
    public async Task Sua_quyen_khong_doi_gi_200_khong_audit_vai_tro_tu_tao_khong_can_xac_nhan()
    {
        var code = IdentitySql.MaVaiTroMoi("FREE");
        var roleId = (short)(await CreateRoleOkAsync(code, "report.create")).GetProperty("roleId").GetInt32();

        using (var same = await SetPermissionsAsync(roleId, ["report.create"]))
            Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(0, await RoleAuditCountAsync("role.permissions", code));

        using (var changed = await SetPermissionsAsync(roleId, ["report.resolve", "post.hide"]))
            Assert.Equal(["post.hide", "report.resolve"], Strings((await AdminTestClient.OkBodyAsync(changed)).GetProperty("permissions")));
        using (var emptied = await SetPermissionsAsync(roleId, []))
            Assert.Empty(Strings((await AdminTestClient.OkBodyAsync(emptied)).GetProperty("permissions")));
        Assert.Equal(2, await RoleAuditCountAsync("role.permissions", code));

        using var manage = await SetPermissionsAsync(roleId, ["role.manage"]);
        await ProblemAsync(manage, HttpStatusCode.BadRequest);   // L-D19
    }

    /// <summary>ROLE-06: sửa quyền ADMIN → 409 <c>system-role</c>, kể cả có <c>confirm</c>.</summary>
    [Fact]
    public async Task ROLE_06_sua_quyen_ADMIN_409_system_role()
    {
        using var response = await SetPermissionsAsync(AdminRoleId, ["post.create"], confirm: true);

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("urn:socialapp:problem:system-role", problem.GetProperty("type").GetString());
        Assert.Empty(await GrantsAsync(AdminRoleId));
    }

    /// <summary>
    /// ROLE-07 (vế qua API): sửa quyền MODERATOR qua API rồi chạy lại migrate + seeder → chỉnh sửa còn nguyên, bảng
    /// <c>permissions</c> vẫn đúng 18 dòng (seeder chỉ bootstrap vai trò CHƯA có dòng nào).
    /// </summary>
    [Fact]
    public async Task ROLE_07_sua_MODERATOR_qua_API_chay_lai_seeder_chinh_sua_con_nguyen()
    {
        var before = await GrantsAsync(ModeratorRoleId);
        var edited = before.Where(c => c != "post.hide").Append("friend.request").Distinct().ToArray();

        try
        {
            using (var response = await SetPermissionsAsync(ModeratorRoleId, edited, confirm: true))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var afterApi = await GrantsAsync(ModeratorRoleId);

            await using (var services = new ServiceCollection().AddIdentityModule(factory.ConnectionString).BuildServiceProvider())
                await services.MigrateIdentityModuleAsync();

            Assert.Equal(afterApi, await GrantsAsync(ModeratorRoleId));
            Assert.DoesNotContain("post.hide", afterApi);
            Assert.Equal(18L, await _client.ScalarAsync<long>("SELECT count(*) FROM identity.permissions"));
        }
        finally
        {
            using var restore = await SetPermissionsAsync(ModeratorRoleId, before, confirm: true);
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }
    }

    /// <summary>
    /// PERM-01 (qua API) ⭐: gỡ <c>post.create</c> của USER (<c>confirm: true</c>) rồi <c>POST /posts</c> NGAY — không tua đồng hồ —
    /// → 403 ở request kế tiếp. Trước đó cùng người đó đăng được (201): 403 chỉ còn một lý do. Không có <c>NotifyAsync</c> thì cache
    /// quyền giữ tập cũ 60 giây và request kế tiếp vẫn 201.
    /// </summary>
    [Fact]
    public async Task PERM_01_go_post_create_cua_USER_qua_API_request_ke_tiep_403()
    {
        var modules = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await modules.PutProfileOkAsync(author, new { displayName = "Người đăng" });
        var post = new { body = "Bài thử quyền.", privacy = "public", mediaKeys = Array.Empty<object>() };
        using (var allowed = await modules.CreatePostAsync(author, post))
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);   // cache quyền của USER đã nạp tập có post.create

        var before = await GrantsAsync(UserRoleId);
        try
        {
            using (var removed = await SetPermissionsAsync(UserRoleId, [.. before.Where(c => c != "post.create")], confirm: true))
                Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

            using var denied = await modules.CreatePostAsync(author, post);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        finally
        {
            using var restore = await SetPermissionsAsync(UserRoleId, before, confirm: true);
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }

        using var again = await modules.CreatePostAsync(author, post);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);   // trả quyền cũng có hiệu lực ngay
    }

    /// <summary>
    /// Tầng 2 <c>role.manage</c> ở mọi action: MODERATOR (và một vai trò tự tạo có mọi quyền gán được — không có <c>role.manage</c>)
    /// → 403 ở cả sáu operation. <c>TC-A05-roles</c> của matrix là bản một dòng của ca này.
    /// </summary>
    [Fact]
    public async Task Moi_action_doi_role_manage_403()
    {
        var almostAll = IdentitySql.MaVaiTroMoi("ALMOST");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, almostAll,
            [.. SocialApp.Modules.Identity.Domain.PermissionCodes.All.Where(c => c != "role.manage")]);

        foreach (var role in new[] { "MODERATOR", almostAll })
        {
            foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                     {
                         (HttpMethod.Get, "/api/v1/admin/roles", null),
                         (HttpMethod.Post, "/api/v1/admin/roles", new { code = "NOPE", displayName = "x", permissions = Array.Empty<string>() }),
                         (HttpMethod.Patch, $"/api/v1/admin/roles/{UserRoleId}", new { displayName = "x" }),
                         (HttpMethod.Put, $"/api/v1/admin/roles/{UserRoleId}/permissions", new { permissions = Array.Empty<string>() }),
                         (HttpMethod.Delete, $"/api/v1/admin/roles/{UserRoleId}", null),
                         (HttpMethod.Get, "/api/v1/admin/permissions", null),
                     })
            {
                using var response = await SendAsync(method, path, body, role);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} {method} {path}: {(int)response.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task Vai_tro_khong_ton_tai_404_id_sai_dang_400()
    {
        using (var missing = await SetPermissionsAsync(32000, ["post.create"]))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var rename = await SendAsync(HttpMethod.Patch, "/api/v1/admin/roles/32000", new { displayName = "x" }))
            Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        using var malformed = await SendAsync(HttpMethod.Delete, "/api/v1/admin/roles/abc");
        var problem = await ProblemAsync(malformed, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("roleId", out _));
    }
}
