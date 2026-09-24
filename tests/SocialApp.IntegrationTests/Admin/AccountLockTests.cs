using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Serilog.Core;
using Serilog.Events;
using SocialApp.IntegrationTests.Auth;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Admin;

/// <summary>
/// GĐ6 D3 — <c>POST /admin/users/{userId}/lock</c>, <c>…/unlock</c> (Đ-6.5–6.7, UC-20) trên Postgres + Redis thật.
///
/// Người bị khóa ĐĂNG NHẬP THẬT (hash BCrypt thật qua <see cref="IdentitySql"/>, <c>/auth/login</c> thật): <c>ADM-01</c> phải chứng
/// minh access token cũ, refresh family và lần đăng nhập sau đều chết — ký token bằng <c>TestJwt</c> thì không có family nào để
/// thu hồi. Admin gọi là một tài khoản ADMIN có thật trong DB (<see cref="_admin"/>): không có Admin hoạt động nào thì bất biến
/// Đ-6.7 chặn MỌI lần khóa bằng 409 — đúng thiết kế, và là lý do lớp này dựng Admin trước tiên.
///
/// Không dùng <see cref="IdentityApiFactory"/>: nó chỉ migrate Identity, còn audit ghi vào <c>moderation.audit_logs</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountLockTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private const string Password = "MatKhauManh123";

    private Guid _admin;

    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);

        _admin = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    // --- Dựng dữ liệu và gọi API -----------------------------------------------------------------------------------------

    private HttpClient Http(WebApplicationFactory<Program>? app = null) =>
        (app ?? factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,                          // tự mang cookie refresh — mỗi "thiết bị" một cookie
            BaseAddress = new Uri("https://localhost"),     // cookie Secure không đi qua http://
        });

    /// <summary>Tài khoản USER đăng nhập được bằng <see cref="Password"/>.</summary>
    private async Task<(Guid Id, string Email)> TaiKhoanDangNhapDuocAsync()
    {
        var email = $"lock-{Guid.NewGuid():N}@test.local";
        var hash = factory.Services.GetRequiredService<IPasswordHasher>().Hash(Password);
        return (await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, email, passwordHash: hash), email);
    }

    private async Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        using var http = Http();
        return await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
    }

    /// <summary>Một "thiết bị": một lần đăng nhập = một refresh family.</summary>
    private async Task<(string Access, string Refresh)> DangNhapAsync(string email)
    {
        using var response = await LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = AuthTestClient.ReadSetCookie(response) ?? throw new InvalidOperationException("login 200 không có cookie");
        return (body.GetProperty("accessToken").GetString()!, cookie.Value.ToString());
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshCookie)
    {
        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthTestClient.RefreshCookieName}={refreshCookie}");
        return await http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> MeAsync(string accessToken)
    {
        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> LockAsync(
        Guid target, object? body = null, Guid? caller = null, string role = "ADMIN", WebApplicationFactory<Program>? app = null)
    {
        using var http = Http(app);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{target}/lock")
        {
            Content = JsonContent.Create(body ?? new { reason = "Vi phạm tiêu chuẩn cộng đồng." }),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(caller ?? _admin, role);
        return await http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> UnlockAsync(Guid target)
    {
        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{target}/unlock");
        request.Headers.Authorization = ModulesTestClient.Bearer(_admin, "ADMIN");
        return await http.SendAsync(request);
    }

    private static async Task<JsonElement> OkBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<T?> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        await cmd.ExecuteNonQueryAsync();
    }

    private Task<string?> StatusAsync(Guid userId) =>
        ScalarAsync<string>("SELECT status FROM identity.users WHERE user_id = $1", userId);

    private Task<long> AuditCountAsync(Guid target, string action) =>
        ScalarAsync<long>("SELECT count(*) FROM moderation.audit_logs WHERE target_id = $1 AND action = $2", target, action);

    /// <summary>
    /// Mốc thu hồi làm tròn xuống GIÂY và so chặt (<c>iat &lt; mốc</c>) — khóa trong cùng giây đăng nhập thì token đó (đúng thiết kế,
    /// Mục 7.5) không chết và ca đỏ ngẫu nhiên. Chờ sang giây kế tiếp của <c>iat</c>, khuôn <c>TokenRevocationTests</c>.
    /// </summary>
    private static async Task QuaGiayCuaAsync(string accessToken)
    {
        var iat = new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetPayloadValue<long>("iat");
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= iat)
            await Task.Delay(50);
    }

    // --- Ca test ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ADM-01: X đăng nhập HAI thiết bị (hai family) rồi bị khóa → <c>disabled</c>; cả hai family có <c>revoked_at</c>; có key
    /// <c>revoked:user:X</c>; access cũ 401; refresh (cả hai cookie) 401; đăng nhập đúng mật khẩu 403 <c>account-disabled</c>, sai
    /// mật khẩu 401; một dòng audit <c>user.lock</c> mang <c>metadata.reason</c> và đúng người thao tác; 200 <c>applied</c>.
    /// </summary>
    [Fact]
    public async Task ADM_01_khoa_X_thu_hoi_moi_family_access_cu_401_refresh_401_login_403()
    {
        var (x, email) = await TaiKhoanDangNhapDuocAsync();
        var phone = await DangNhapAsync(email);
        var laptop = await DangNhapAsync(email);
        using (var before = await MeAsync(phone.Access))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await QuaGiayCuaAsync(laptop.Access);

        using var locked = await LockAsync(x, new { reason = "  Lừa đảo lặp lại.  " });

        var body = await OkBodyAsync(locked);
        Assert.Equal("applied", body.GetProperty("revocation").GetString());
        Assert.Equal("disabled", body.GetProperty("user").GetProperty("status").GetString());
        Assert.Equal(x, body.GetProperty("user").GetProperty("userId").GetGuid());

        Assert.Equal("disabled", await StatusAsync(x));
        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT count(DISTINCT family_id) FROM identity.refresh_tokens WHERE user_id = $1", x));
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT count(*) FROM identity.refresh_tokens WHERE user_id = $1 AND revoked_at IS NULL", x));
        Assert.True(await redis.Database.KeyExistsAsync($"revoked:user:{x}"));

        foreach (var access in new[] { phone.Access, laptop.Access })
        {
            using var me = await MeAsync(access);
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        foreach (var refresh in new[] { phone.Refresh, laptop.Refresh })
        {
            using var refreshed = await RefreshAsync(refresh);
            Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        }

        using (var right = await LoginAsync(email, Password))
        {
            Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode);
            using var problem = JsonDocument.Parse(await right.Content.ReadAsStringAsync());
            Assert.Equal("urn:socialapp:problem:account-disabled", problem.RootElement.GetProperty("type").GetString());
        }
        using (var wrong = await LoginAsync(email, Password + "sai"))
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        Assert.Equal(1, await AuditCountAsync(x, "user.lock"));
        Assert.Equal("Lừa đảo lặp lại.", await ScalarAsync<string>(
            "SELECT metadata->>'reason' FROM moderation.audit_logs WHERE target_id = $1 AND action = 'user.lock'", x));
        Assert.Equal(_admin, await ScalarAsync<Guid>(
            "SELECT actor_id FROM moderation.audit_logs WHERE target_id = $1 AND action = 'user.lock'", x));
        Assert.Equal("user", await ScalarAsync<string>(
            "SELECT target_type FROM moderation.audit_logs WHERE target_id = $1 AND action = 'user.lock'", x));
        Assert.Null(await ScalarAsync<string>(
            "SELECT metadata->>'revocation' FROM moderation.audit_logs WHERE target_id = $1 AND action = 'user.lock'", x));   // L-D9
    }

    /// <summary>ADM-01b (L-D10): khóa lần hai → 200 <c>not-needed</c>, vẫn MỘT dòng audit, không ghi lại mốc Redis.</summary>
    [Fact]
    public async Task ADM_01b_khoa_lan_hai_200_not_needed_van_mot_dong_audit()
    {
        var (x, _) = await TaiKhoanDangNhapDuocAsync();
        using (var first = await LockAsync(x))
            Assert.Equal("applied", (await OkBodyAsync(first)).GetProperty("revocation").GetString());
        await redis.Database.KeyDeleteAsync($"revoked:user:{x}");

        using var second = await LockAsync(x);

        var body = await OkBodyAsync(second);
        Assert.Equal("not-needed", body.GetProperty("revocation").GetString());
        Assert.Equal("disabled", body.GetProperty("user").GetProperty("status").GetString());
        Assert.Equal(1, await AuditCountAsync(x, "user.lock"));
        Assert.False(await redis.Database.KeyExistsAsync($"revoked:user:{x}"));
    }

    /// <summary>
    /// ADM-02: mở khóa X đang <c>disabled</c> VÀ đang bị khóa tạm FR-003 (<c>locked_until</c> tương lai, bộ đếm 4) → đăng nhập được
    /// ngay; <c>locked_until</c> null, bộ đếm 0; một dòng audit <c>user.unlock</c>; <c>not-needed</c>. Mở lần hai: không đổi, không audit.
    /// </summary>
    [Fact]
    public async Task ADM_02_mo_khoa_xoa_ca_disabled_lan_khoa_tam_dang_nhap_duoc_ngay()
    {
        var (x, email) = await TaiKhoanDangNhapDuocAsync();
        using (var locked = await LockAsync(x))
            Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        await ExecAsync(
            "UPDATE identity.users SET locked_until = now() + interval '10 minutes', failed_login_count = 4 WHERE user_id = $1", x);

        using var unlocked = await UnlockAsync(x);

        var body = await OkBodyAsync(unlocked);
        Assert.Equal("not-needed", body.GetProperty("revocation").GetString());
        Assert.Equal("active", body.GetProperty("user").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("user").GetProperty("lockedUntil").ValueKind);
        Assert.Null(await ScalarAsync<DateTime?>("SELECT locked_until FROM identity.users WHERE user_id = $1", x));
        Assert.Equal((short)0, await ScalarAsync<short>("SELECT failed_login_count FROM identity.users WHERE user_id = $1", x));
        Assert.Equal(1, await AuditCountAsync(x, "user.unlock"));

        using (var login = await LoginAsync(email, Password))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var again = await UnlockAsync(x);
        Assert.Equal("not-needed", (await OkBodyAsync(again)).GetProperty("revocation").GetString());
        Assert.Equal(1, await AuditCountAsync(x, "user.unlock"));
    }

    /// <summary>Mở khóa tài khoản chỉ đang bị khóa tạm FR-003 (vẫn <c>active</c>) cũng là một thay đổi: xóa mốc, có audit.</summary>
    [Fact]
    public async Task Mo_khoa_tai_khoan_chi_bi_khoa_tam_FR003_xoa_moc_co_audit()
    {
        var (x, email) = await TaiKhoanDangNhapDuocAsync();
        await ExecAsync(
            "UPDATE identity.users SET locked_until = now() + interval '10 minutes', failed_login_count = 0 WHERE user_id = $1", x);
        using (var blocked = await LoginAsync(email, Password))
            Assert.Equal((HttpStatusCode)423, blocked.StatusCode);

        using var unlocked = await UnlockAsync(x);

        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.Equal(1, await AuditCountAsync(x, "user.unlock"));
        using var login = await LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>ADM-03: Admin tự khóa mình → 400 <c>errors.userId</c>; DB không đổi; 0 dòng audit.</summary>
    [Fact]
    public async Task ADM_03_tu_khoa_minh_400_khong_doi_gi()
    {
        var self = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);

        using var response = await LockAsync(self, caller: self);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["userId"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal("active", await StatusAsync(self));
        Assert.Equal(0, await AuditCountAsync(self, "user.lock"));
    }

    /// <summary>
    /// ADM-04 (vế khóa): khóa Admin hoạt động CUỐI CÙNG → 409 <c>last-admin</c>; DB không đổi (status, refresh family); KHÔNG có
    /// key <c>revoked:user</c> (Redis chỉ ghi sau COMMIT — cạm bẫy 4); 0 dòng audit. Người gọi là vai trò tự tạo chỉ có
    /// <c>user.lock</c>: Admin khác đều đã <c>disabled</c>, và một Admin bị khóa thì không gọi được API.
    /// </summary>
    [Fact]
    public async Task ADM_04_khoa_Admin_hoat_dong_cuoi_cung_409_khong_doi_gi()
    {
        var hash = factory.Services.GetRequiredService<IPasswordHasher>().Hash(Password);
        var lastEmail = $"last-admin-{Guid.NewGuid():N}@test.local";
        var last = await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, lastEmail, "ADMIN", passwordHash: hash);
        await ExecAsync("""
            UPDATE identity.users SET status = 'disabled'
             WHERE user_id <> $1 AND role_id = (SELECT role_id FROM identity.roles WHERE code = 'ADMIN')
            """, last);
        var session = await DangNhapAsync(lastEmail);
        var locker = IdentitySql.MaVaiTroMoi("LOCKER");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, locker, "user.lock");

        try
        {
            using var response = await LockAsync(last, caller: Guid.NewGuid(), role: locker);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("urn:socialapp:problem:last-admin", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("active", await StatusAsync(last));
            Assert.Equal(0, await ScalarAsync<long>(
                "SELECT count(*) FROM identity.refresh_tokens WHERE user_id = $1 AND revoked_at IS NOT NULL", last));
            Assert.False(await redis.Database.KeyExistsAsync($"revoked:user:{last}"));
            Assert.Equal(0, await AuditCountAsync(last, "user.lock"));
            using var me = await MeAsync(session.Access);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            // Trả Admin của lớp về hoạt động — ca khác trong lớp khóa USER dưới quyền của nó.
            await ExecAsync("UPDATE identity.users SET status = 'active' WHERE user_id = $1", _admin);
        }
    }

    /// <summary>
    /// ADM-06: ghi Redis hỏng SAU COMMIT (kho thu hồi giả ném ở <c>RevokeUserAsync</c>) → 200 <c>deferred</c>; DB ĐÃ đổi; metric
    /// <c>socialapp_revocation_failures_total</c> +1; log Error có <c>userId</c>, KHÔNG có email. Kho giả vẫn trả <c>NotRevoked</c> ở
    /// <c>CheckAsync</c> — không thì mọi request 401/503 (cạm bẫy 6).
    /// </summary>
    [Fact]
    public async Task ADM_06_Redis_hong_sau_COMMIT_200_deferred_DB_da_doi_metric_va_log_Error()
    {
        var (x, email) = await TaiKhoanDangNhapDuocAsync();
        var logs = new CapturingLogSink();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ITokenRevocationStore>();
            s.AddSingleton<ITokenRevocationStore, ThrowingRevokeStore>();
            s.AddSingleton<ILogEventSink>(logs);
        }));
        var before = await RevocationFailuresAsync(app);

        using var response = await LockAsync(x, app: app);

        var body = await OkBodyAsync(response);
        Assert.Equal("deferred", body.GetProperty("revocation").GetString());
        Assert.Equal("disabled", await StatusAsync(x));
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT count(*) FROM identity.refresh_tokens WHERE user_id = $1 AND revoked_at IS NULL", x));
        Assert.Equal(1, await AuditCountAsync(x, "user.lock"));
        Assert.Equal(before + 1, await RevocationFailuresAsync(app));
        Assert.Equal(3, ThrowingRevokeStore.Attempts(x));

        var error = Assert.Single(logs.Events, e =>
            e.Level == LogEventLevel.Error && e.MessageTemplate.Text.StartsWith("Không ghi được mốc thu hồi", StringComparison.Ordinal));
        var rendered = error.RenderMessage();
        Assert.Contains(x.ToString(), rendered);
        Assert.DoesNotContain(email, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", rendered);
    }

    /// <summary><c>reason</c> thiếu, rỗng sau cắt khoảng trắng, hay dài hơn 500 → 400 <c>errors.reason</c>; không đổi gì.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"reason\":\"   \"}")]
    [InlineData("{\"reason\":null}")]
    [InlineData("TOO_LONG")]
    public async Task Ly_do_sai_400_errors_reason(string json)
    {
        var (x, _) = await TaiKhoanDangNhapDuocAsync();
        if (json == "TOO_LONG")
            json = JsonSerializer.Serialize(new { reason = new string('x', 501) });

        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{x}/lock")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(_admin, "ADMIN");
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("reason", out _), problem.RootElement.GetRawText());
        Assert.Equal("active", await StatusAsync(x));
    }

    /// <summary>500 ký tự sau cắt là biên hợp lệ — khoảng trắng hai đầu không tính.</summary>
    [Fact]
    public async Task Ly_do_500_ky_tu_sau_cat_hop_le()
    {
        var (x, _) = await TaiKhoanDangNhapDuocAsync();

        using var response = await LockAsync(x, new { reason = "  " + new string('x', 500) + "  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Đích không tồn tại → 404 cho cả khóa lẫn mở; id sai dạng → 400 <c>errors.userId</c>.</summary>
    [Fact]
    public async Task Dich_khong_ton_tai_404_sai_dang_400()
    {
        using (var missingLock = await LockAsync(Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, missingLock.StatusCode);
        using (var missingUnlock = await UnlockAsync(Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, missingUnlock.StatusCode);

        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/users/khong-phai-uuid/unlock");
        request.Headers.Authorization = ModulesTestClient.Bearer(_admin, "ADMIN");
        using var malformed = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    /// <summary>Tầng 2 riêng từng action: chỉ có <c>user.unlock</c> thì mở được mà KHÔNG khóa được (403).</summary>
    [Fact]
    public async Task Moi_action_mot_ma_quyen_rieng()
    {
        var (x, _) = await TaiKhoanDangNhapDuocAsync();
        var unlocker = IdentitySql.MaVaiTroMoi("UNLOCKER");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, unlocker, "user.unlock");

        using var locked = await LockAsync(x, caller: Guid.NewGuid(), role: unlocker);
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);

        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{x}/unlock");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), unlocker);
        using var unlocked = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    private static async Task<double> RevocationFailuresAsync(WebApplicationFactory<Program> app)
    {
        using var http = app.CreateClient();
        var line = (await http.GetStringAsync("/metrics")).Split('\n')
            .Single(l => l.StartsWith("socialapp_revocation_failures_total ", StringComparison.Ordinal));
        return double.Parse(line["socialapp_revocation_failures_total ".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Kho thu hồi mà bên GHI luôn hỏng; bên đọc trả lời bình thường "chưa thu hồi".</summary>
    private sealed class ThrowingRevokeStore : ITokenRevocationStore
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> Calls = new();

        public static int Attempts(Guid userId) => Calls.GetValueOrDefault(userId);

        public Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default)
        {
            Calls.AddOrUpdate(userId, 1, (_, n) => n + 1);
            throw new InvalidOperationException("Redis giả: không ghi được.");
        }

        public Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default) => Task.FromResult(false);

        public Task<RevocationCheck> CheckAsync(string userId, long issuedAtUnix, CancellationToken ct = default) =>
            Task.FromResult(RevocationCheck.NotRevoked);
    }
}
