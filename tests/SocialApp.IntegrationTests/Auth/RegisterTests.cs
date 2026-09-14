using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application.Email;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D1 — <c>POST /auth/register</c> (FR-001 nửa đầu) trên Postgres thật. Kỳ vọng viết tay theo hợp đồng và Mục 12:
/// cost "12", SHA-256 tính bằng BCL, vai trò "USER" — cố ý không đọc SecureToken/RoleCodes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegisterTests(PostgresFixture postgres, IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string ValidEmailPlaceholder = "<email-hop-le>";

    private AuthTestClient _auth = null!;

    public async Task InitializeAsync()
    {
        await factory.UseFreshDatabaseAsync(postgres);
        _auth = new AuthTestClient(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dang_ky_hop_le_201_userId_UUIDv7_va_dung_mot_mail()
    {
        var email = AuthTestClient.NewEmail();

        using var response = await _auth.RegisterAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var userId = body.GetProperty("userId").GetString()!;
        Assert.True(Guid.TryParse(userId, out _));
        Assert.Equal('7', userId[14]);   // "xxxxxxxx-xxxx-7xxx-…": nibble phiên bản của UUID v7
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task DB_luu_hash_BCrypt_cost_12_vai_tro_USER_chua_xac_minh()
    {
        var userId = await RegisterAsync(AuthTestClient.NewEmail());

        var row = await _auth.QueryRowAsync("""
            SELECT u.password_hash, r.code, u.email_verified_at
              FROM identity.users u JOIN identity.roles r ON r.role_id = u.role_id
             WHERE u.user_id = $1
            """, userId);

        Assert.NotNull(row);
        var hash = (string)row["password_hash"]!;
        Assert.StartsWith("$2", hash);
        Assert.Equal("12", hash.Split('$')[2]);
        Assert.NotEqual(AuthTestClient.Password, hash);
        Assert.Equal("USER", row["code"]);
        Assert.Null(row["email_verified_at"]);
    }

    [Fact]
    public async Task Token_xac_minh_luu_bam_SHA256_han_24h_khong_cot_nao_chua_ban_ro()
    {
        var email = AuthTestClient.NewEmail();
        var before = DateTimeOffset.UtcNow;
        var userId = await RegisterAsync(email);
        var plain = factory.Emails.LatestTokenFor(email);

        var token = await _auth.QueryRowAsync("SELECT * FROM identity.email_verification_tokens WHERE user_id = $1", userId);
        var user = await _auth.QueryRowAsync("SELECT * FROM identity.users WHERE user_id = $1", userId);

        Assert.Matches("^[0-9a-f]{64}$", plain);
        Assert.NotNull(token);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant(), token["token_hash"]);
        Assert.Null(token["consumed_at"]);
        var expiresAt = new DateTimeOffset((DateTime)token["expires_at"]!);
        Assert.InRange(expiresAt, before.AddHours(24).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(24).AddMinutes(1));
        Assert.DoesNotContain(token.Values.Concat(user!.Values), v => v?.ToString()?.Contains(plain, StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Email_trung_khac_hoa_thuong_409_problem_json_khong_lo_email_khong_gui_mail_thu_hai()
    {
        var email = AuthTestClient.NewEmail();
        await RegisterAsync(email);

        using var response = await _auth.RegisterAsync(email.ToUpperInvariant(), AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(JsonDocument.Parse(text).RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(email, text, StringComparison.OrdinalIgnoreCase);
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task Hai_request_cung_email_song_song_mot_201_mot_409_khong_500()
    {
        var email = AuthTestClient.NewEmail();

        var responses = await Task.WhenAll(
            _auth.RegisterAsync(email, AuthTestClient.Password),
            _auth.RegisterAsync(email, AuthTestClient.Password));
        try
        {
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        var count = await _auth.QueryRowAsync("SELECT count(*) AS n FROM identity.users WHERE email = $1::citext", email);
        Assert.Equal(1L, count!["n"]);
    }

    public static TheoryData<string, string, string> InvalidInputs => new()
    {
        { ValidEmailPlaceholder, "1234567", "password" },                                      // 7 ký tự
        { ValidEmailPlaceholder, new string('a', 73), "password" },                            // 73 byte
        { ValidEmailPlaceholder, string.Concat(Enumerable.Repeat("ấ", 25)), "password" },      // 25 ký tự nhưng 75 byte UTF-8
        { "khong-phai-email", AuthTestClient.Password, "email" },
        { "Nguyen An <an@example.com>", AuthTestClient.Password, "email" },
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Du_lieu_sai_400_errors_camelCase_co_traceId(string email, string password, string field)
    {
        var target = email == ValidEmailPlaceholder ? AuthTestClient.NewEmail() : email;

        using var response = await _auth.RegisterAsync(target, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"errors không có key '{field}': {problem}");
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Empty(factory.Emails.SentTo(target));
    }

    [Fact]
    public async Task Body_co_field_la_role_ADMIN_400_va_khong_tao_user()
    {
        var email = AuthTestClient.NewEmail();

        using var response = await _auth.Http.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = AuthTestClient.Password, role = "ADMIN" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var count = await _auth.QueryRowAsync("SELECT count(*) AS n FROM identity.users WHERE email = $1::citext", email);
        Assert.Equal(0L, count!["n"]);
    }

    /// <summary>Đ-D5: SMTP hỏng → rollback → 500, và người dùng đăng ký lại được (không có dòng users nào kẹt lại).</summary>
    [Fact]
    public async Task Gui_mail_hong_500_va_khong_con_dong_users_nao()
    {
        var email = AuthTestClient.NewEmail();
        using var broken = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IEmailSender, ThrowingEmailSender>()));
        using var client = broken.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthTestClient.Password });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var count = await _auth.QueryRowAsync("SELECT count(*) AS n FROM identity.users WHERE email = $1::citext", email);
        Assert.Equal(0L, count!["n"]);
    }

    /// <summary>
    /// Đ-D7: 10 req/phút/IP cho cả nhóm auth. Cùng một IP qua header của <see cref="FakeRemoteIpStartupFilter"/>; body sai
    /// để 10 request đầu dừng ở 400, không tốn BCrypt.
    /// </summary>
    [Fact]
    public async Task Auth_rate_limit_429()
    {
        var ip = $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 11; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
            {
                Content = JsonContent.Create(new { email = "khong-phai-email", password = "x" }),
            };
            request.Headers.Add(FakeRemoteIpStartupFilter.Header, ip);
            using var response = await _auth.Http.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.BadRequest, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    private async Task<Guid> RegisterAsync(string email)
    {
        using var response = await _auth.RegisterAsync(email, AuthTestClient.Password);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("userId").GetGuid();
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendVerificationAsync(string toEmail, string plainToken, CancellationToken ct) =>
            throw new InvalidOperationException("SMTP không tới được (giả lập).");
    }
}
