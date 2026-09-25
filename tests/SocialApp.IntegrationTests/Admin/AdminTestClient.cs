using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using SocialApp.IntegrationTests.Auth;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application.Security;
using Xunit;

namespace SocialApp.IntegrationTests.Admin;

/// <summary>
/// Harness chung của test màn quản trị từ D4 (B1 — L-D7): người bị đổi ĐĂNG NHẬP THẬT (hash BCrypt thật, <c>/auth/login</c> thật,
/// mỗi lần đăng nhập một refresh family), Admin gọi bằng token <c>TestJwt</c>, đọc DB bằng SQL. <c>AccountLockTests</c> (D3) có bản
/// riêng viết trước lớp này — cùng hành vi, để nguyên cho khỏi đụng ca đã xanh.
/// </summary>
public sealed class AdminTestClient(ModulesApiFactory factory)
{
    public const string Password = "MatKhauManh123";

    public HttpClient Http() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,                          // tự mang cookie refresh — mỗi "thiết bị" một cookie
        BaseAddress = new Uri("https://localhost"),     // cookie Secure không đi qua http://
    });

    /// <summary>Tài khoản đăng nhập được bằng <see cref="Password"/>, vai trò <paramref name="roleCode"/>.</summary>
    public async Task<(Guid Id, string Email)> TaiKhoanDangNhapDuocAsync(string roleCode = "USER", string tag = "acc")
    {
        var email = $"{tag}-{Guid.NewGuid():N}@test.local";
        var hash = factory.Services.GetRequiredService<IPasswordHasher>().Hash(Password);
        return (await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, email, roleCode, passwordHash: hash), email);
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password = Password)
    {
        using var http = Http();
        return await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
    }

    /// <summary>Một "thiết bị": một lần đăng nhập = một refresh family.</summary>
    public async Task<(string Access, string Refresh)> DangNhapAsync(string email)
    {
        using var response = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = AuthTestClient.ReadSetCookie(response) ?? throw new InvalidOperationException("login 200 không có cookie");
        return (body.GetProperty("accessToken").GetString()!, cookie.Value.ToString());
    }

    public async Task<HttpResponseMessage> RefreshAsync(string refreshCookie)
    {
        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthTestClient.RefreshCookieName}={refreshCookie}");
        return await http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> MeAsync(string accessToken)
    {
        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await http.SendAsync(request);
    }

    /// <summary><c>PUT /admin/users/{target}/role</c> bằng token <c>TestJwt</c> của (<paramref name="caller"/>, <paramref name="role"/>).</summary>
    public async Task<HttpResponseMessage> AssignRoleAsync(Guid target, object body, Guid caller, string role = "ADMIN")
    {
        using var http = Http();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/users/{target}/role")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(caller, role);
        return await http.SendAsync(request);
    }

    public static async Task<JsonElement> OkBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static string? ClaimOf(string accessToken, string claim) =>
        new JsonWebTokenHandler().ReadJsonWebToken(accessToken).TryGetPayloadValue<string>(claim, out var value) ? value : null;

    /// <summary>
    /// Mốc thu hồi làm tròn xuống GIÂY và so chặt (<c>iat &lt; mốc</c>) — thao tác trong cùng giây đăng nhập thì token đó (đúng thiết
    /// kế, Mục 7.5) không chết và ca đỏ ngẫu nhiên. Chờ sang giây kế tiếp của <c>iat</c>, khuôn <c>TokenRevocationTests</c>.
    /// </summary>
    public static async Task QuaGiayCuaAsync(string accessToken)
    {
        var iat = new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetPayloadValue<long>("iat");
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= iat)
            await Task.Delay(50);
    }

    public async Task<T?> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        await cmd.ExecuteNonQueryAsync();
    }

    public Task<string?> RoleCodeAsync(Guid userId) => ScalarAsync<string>(
        "SELECT r.code FROM identity.users u JOIN identity.roles r ON r.role_id = u.role_id WHERE u.user_id = $1", userId);

    public Task<long> AuditCountAsync(Guid target, string action) => ScalarAsync<long>(
        "SELECT count(*) FROM moderation.audit_logs WHERE target_id = $1 AND action = $2", target, action);

    /// <summary>Mọi Admin khác <paramref name="except"/> thành <c>disabled</c> — dựng cảnh "Admin hoạt động cuối cùng".</summary>
    public Task DisableOtherAdminsAsync(Guid except) => ExecAsync("""
        UPDATE identity.users SET status = 'disabled'
         WHERE user_id <> $1 AND role_id = (SELECT role_id FROM identity.roles WHERE code = 'ADMIN')
        """, except);
}
