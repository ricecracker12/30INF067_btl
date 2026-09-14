using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D7 — <c>GET /me</c> trên Postgres thật, token lấy bằng đăng nhập thật (không TestJwt: sub ngẫu nhiên không có trong DB).
/// Kỳ vọng viết tay theo hợp đồng (<c>MeResponse</c>) và Mục 5.1 (<c>"USER"</c> / <c>"Người dùng"</c>). "Không token → 401" là
/// dòng <c>TC-A01-me</c> của AuthZ matrix, không lặp lại ở đây.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MeTests(PostgresFixture postgres, IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private AuthTestClient _auth = null!;

    public async Task InitializeAsync()
    {
        await factory.UseFreshDatabaseAsync(postgres);
        _auth = new AuthTestClient(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dang_nhap_roi_goi_me_200_dung_7_truong_doc_tu_DB()
    {
        var (userId, email, accessToken) = await LoggedInUserAsync();

        using var response = await _auth.GetMeAsync(accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Đúng tập trường của hợp đồng — không passwordHash, không cột nào khác của users lọt ra.
        Assert.Equal(
            ["createdAt", "email", "emailVerifiedAt", "role", "roleDisplayName", "status", "userId"],
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(userId, body.GetProperty("userId").GetGuid());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("USER", body.GetProperty("role").GetString());
        Assert.Equal("Người dùng", body.GetProperty("roleDisplayName").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("emailVerifiedAt").ValueKind);
        Assert.InRange(body.GetProperty("createdAt").GetDateTimeOffset(),
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    /// <summary>Quyết định 3: roles tách code (bất biến) và display_name (sửa được). /me đọc cả hai từ DB.</summary>
    [Fact]
    public async Task Doi_display_name_bang_SQL_me_tra_ten_moi_role_van_USER()
    {
        var (_, _, accessToken) = await LoggedInUserAsync();

        await _auth.ExecuteSqlAsync("UPDATE identity.roles SET display_name = $1 WHERE code = 'USER'", "Thành viên");
        try
        {
            using var response = await _auth.GetMeAsync(accessToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Thành viên", body.GetProperty("roleDisplayName").GetString());
            Assert.Equal("USER", body.GetProperty("role").GetString());
        }
        finally
        {
            // Các test khác trong lớp dùng chung database và kỳ vọng "Người dùng".
            await _auth.ExecuteSqlAsync("UPDATE identity.roles SET display_name = $1 WHERE code = 'USER'", "Người dùng");
        }
    }

    /// <summary>Token đúng chữ ký nhưng user không còn: 401 (hợp đồng /me chỉ có 200/401), không 404, không 500.</summary>
    [Fact]
    public async Task User_da_bi_xoa_token_cu_401_problem_json()
    {
        var (userId, _, accessToken) = await LoggedInUserAsync();
        await _auth.ExecuteSqlAsync("DELETE FROM identity.users WHERE user_id = $1", userId);

        using var response = await _auth.GetMeAsync(accessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    /// <summary>
    /// /me dùng hạn mức chung theo user, KHÔNG phải policy "auth" 10 req/phút/IP. Gắn nhầm thì FE gọi /me vài lần khi tải
    /// trang là 429. Cùng một IP qua header của <see cref="FakeRemoteIpStartupFilter"/> để policy "auth" (nếu có) cắn được.
    /// </summary>
    [Fact]
    public async Task Me_khong_dung_han_muc_auth_11_request_cung_IP_deu_200()
    {
        var (_, _, accessToken) = await LoggedInUserAsync();
        var ip = $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";

        for (var attempt = 1; attempt <= 11; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add(FakeRemoteIpStartupFilter.Header, ip);
            using var response = await _auth.Http.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"request {attempt}: nhận {(int)response.StatusCode}");
        }
    }

    private async Task<(Guid UserId, string Email, string AccessToken)> LoggedInUserAsync()
    {
        var email = AuthTestClient.NewEmail();
        var userId = await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);
        var (accessToken, _) = await _auth.LoginAsync(email, AuthTestClient.Password);
        return (userId, email, accessToken);
    }
}
