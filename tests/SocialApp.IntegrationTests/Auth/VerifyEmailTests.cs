using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D2 — <c>POST /auth/verify-email</c> (FR-001 nửa sau) trên Postgres thật. 400 (link sai) tách khỏi 410 (hết hạn / đã
/// dùng) để FE hiển thị khác nhau. "Hết hạn" tạo bằng cách lùi <c>expires_at</c> trong DB (Đ-D10), không làm giả đồng hồ.
/// Chuỗi "verify xong thì login hết 403" kiểm ở D3 (AC-01).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VerifyEmailTests(PostgresFixture postgres, IdentityApiFactory factory)
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
    public async Task Token_trong_mail_200_va_DB_danh_dau_da_xac_minh_va_da_dung()
    {
        var (userId, email, token) = await RegisterAsync();
        var before = DateTimeOffset.UtcNow;

        using var response = await _auth.VerifyEmailAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
        var verifiedAt = body.GetProperty("verifiedAt").GetDateTimeOffset();
        Assert.InRange(verifiedAt, before.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

        var row = await _auth.QueryRowAsync("""
            SELECT u.email_verified_at, t.consumed_at
              FROM identity.users u JOIN identity.email_verification_tokens t ON t.user_id = u.user_id
             WHERE u.user_id = $1
            """, userId);
        Assert.NotNull(row);
        Assert.NotNull(row["consumed_at"]);
        // Postgres giữ tới micro giây — lệch dưới 1 ms là cùng một mốc.
        var storedAt = new DateTimeOffset((DateTime)row["email_verified_at"]!);
        Assert.True(Math.Abs((storedAt - verifiedAt).TotalMilliseconds) < 1, $"DB {storedAt:O} vs response {verifiedAt:O}");
    }

    [Fact]
    public async Task Token_dung_dinh_dang_nhung_khong_ton_tai_400_khong_phai_410()
    {
        var unknown = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        using var response = await _auth.VerifyEmailAsync(unknown);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ReadProblemAsync(response, "Dữ liệu không hợp lệ");
    }

    public static TheoryData<string> MalformedTokens => new()
    {
        "abc",
        "",
        new string('a', 63),
        new string('a', 65),
        new string('A', 64),   // hex HOA: SecureToken chỉ sinh hex thường
        new string('g', 64),
    };

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    public async Task Token_sai_dinh_dang_400_co_errors_token(string token)
    {
        using var response = await _auth.VerifyEmailAsync(token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response, "Dữ liệu không hợp lệ");
        Assert.True(problem.TryGetProperty("errors", out var errors) && errors.TryGetProperty("token", out _),
            $"errors không có key 'token': {problem}");
    }

    [Fact]
    public async Task Token_het_han_410_va_user_van_chua_xac_minh()
    {
        var (userId, email, token) = await RegisterAsync();
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.email_verification_tokens SET expires_at = now() - interval '1 second' WHERE user_id = $1", userId);

        using var response = await _auth.VerifyEmailAsync(token);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        await ReadProblemAsync(response, "Liên kết không còn hiệu lực");
        Assert.DoesNotContain(email, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var row = await _auth.QueryRowAsync("SELECT email_verified_at FROM identity.users WHERE user_id = $1", userId);
        Assert.Null(row!["email_verified_at"]);
    }

    [Fact]
    public async Task Verify_lan_hai_410()
    {
        var (_, _, token) = await RegisterAsync();

        using var first = await _auth.VerifyEmailAsync(token);
        using var second = await _auth.VerifyEmailAsync(token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
    }

    [Fact]
    public async Task Hai_request_cung_token_song_song_dung_mot_200_mot_410()
    {
        var (_, _, token) = await RegisterAsync();

        var responses = await Task.WhenAll(_auth.VerifyEmailAsync(token), _auth.VerifyEmailAsync(token));
        try
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Gone], responses.Select(r => r.StatusCode).Order());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    /// <summary>D3 trở đi dựng user đã xác minh bằng helper này — khóa nó chạy được ngay khi có verify-email.</summary>
    [Fact]
    public async Task Helper_RegisterAndVerifyAsync_cua_harness_tao_user_da_xac_minh()
    {
        var userId = await _auth.RegisterAndVerifyAsync(AuthTestClient.NewEmail(), AuthTestClient.Password);

        var row = await _auth.QueryRowAsync("SELECT email_verified_at FROM identity.users WHERE user_id = $1", userId);
        Assert.NotNull(row!["email_verified_at"]);
    }

    private async Task<(Guid UserId, string Email, string Token)> RegisterAsync()
    {
        var email = AuthTestClient.NewEmail();
        using var response = await _auth.RegisterAsync(email, AuthTestClient.Password);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("userId").GetGuid(), email, factory.Emails.LatestTokenFor(email));
    }

    /// <summary>Title bắt buộc truyền: hợp đồng <c>required: [title, status, traceId]</c>, 410 từng ra KHÔNG có title (D9).</summary>
    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, string expectedTitle)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal(expectedTitle, problem.TryGetProperty("title", out var title) ? title.GetString() : null);
        return problem;
    }
}
