using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D3 — <c>POST /auth/login</c> + lockout (FR-002, FR-003) trên Postgres thật. Kỳ vọng viết tay theo hợp đồng và
/// giai-doan-1.md Mục 7.2: 900 giây, vai trò "USER", 5 lần sai liên tiếp, SHA-256 bằng BCL. "Hết khóa" tạo bằng cách lùi
/// <c>locked_until</c> trong DB (Đ-D10), không làm giả đồng hồ.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LoginTests(PostgresFixture postgres, IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string WrongPassword = "SaiMatKhau999";

    private AuthTestClient _auth = null!;

    public async Task InitializeAsync()
    {
        await factory.UseFreshDatabaseAsync(postgres);
        _auth = new AuthTestClient(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AC01_dang_nhap_dung_200_token_qua_tang_1_va_luu_bam_refresh_token()
    {
        var (userId, email) = await VerifiedUserAsync();

        using var response = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        Assert.False(body.TryGetProperty("refreshToken", out _), "refresh token chỉ được đi trong cookie");
        var accessToken = body.GetProperty("accessToken").GetString()!;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        Assert.Equal(userId.ToString(), jwt.GetPayloadValue<string>("sub"));
        Assert.Equal("USER", jwt.GetPayloadValue<string>("role"));

        // Token vừa phát dùng được thật: qua tầng 1 và chạm tới DB (D7 — trước đó gọi route không tồn tại → 404).
        using var me = await _auth.GetMeAsync(accessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(userId, (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid());

        var cookie = AuthTestClient.ReadSetCookie(response);
        Assert.NotNull(cookie);
        var plain = cookie.Value.ToString();
        Assert.Matches("^[0-9a-f]{64}$", plain);

        var count = await _auth.QueryRowAsync("SELECT count(*) AS n FROM identity.refresh_tokens WHERE user_id = $1", userId);
        Assert.Equal(1L, count!["n"]);
        var row = await _auth.QueryRowAsync("SELECT * FROM identity.refresh_tokens WHERE user_id = $1", userId);
        Assert.NotNull(row);
        Assert.Equal(Sha256Hex(plain), row["token_hash"]);
        Assert.NotNull(row["family_id"]);
        Assert.NotNull(row["created_ip"]);
        Assert.Null(row["revoked_at"]);
        Assert.Null(row["replaced_by_id"]);
        var expiresAt = new DateTimeOffset((DateTime)row["expires_at"]!);
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));
        Assert.DoesNotContain(row.Values, v => v?.ToString()?.Contains(plain, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Bẫy citext: so email bằng tham số text thì phân biệt hoa thường mà không lỗi.</summary>
    [Fact]
    public async Task AC01b_dang_nhap_bang_email_khac_hoa_thuong_200()
    {
        var email = "An.Nguyen." + AuthTestClient.NewEmail();
        await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);

        using var response = await _auth.PostLoginAsync(email.ToLowerInvariant(), AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC02_sai_mat_khau_401_va_bo_dem_bang_1()
    {
        var (userId, email) = await VerifiedUserAsync();

        using var response = await _auth.PostLoginAsync(email, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await ReadProblemAsync(response);
        Assert.Null(AuthTestClient.ReadSetCookie(response));
        var row = await _auth.QueryRowAsync("SELECT failed_login_count FROM identity.users WHERE user_id = $1", userId);
        Assert.Equal((short)1, row!["failed_login_count"]);
    }

    /// <summary>AC-02: response của "sai mật khẩu" và "email không tồn tại" giống hệt nhau, trừ phần riêng của từng request.</summary>
    [Fact]
    public async Task AC02b_email_khong_ton_tai_va_sai_mat_khau_response_giong_het()
    {
        var (_, email) = await VerifiedUserAsync();

        using var wrongPassword = await _auth.PostLoginAsync(email, WrongPassword);
        using var unknownEmail = await _auth.PostLoginAsync(AuthTestClient.NewEmail(), WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        Assert.Equal(await BodyWithoutPerRequestFieldsAsync(wrongPassword), await BodyWithoutPerRequestFieldsAsync(unknownEmail));
        Assert.Equal(HeadersWithoutCorrelationId(wrongPassword), HeadersWithoutCorrelationId(unknownEmail));
    }

    /// <summary>
    /// Lưới PHỤ chống quên BCrypt giả (lưới chính là LoginServiceTests). So thô: trung vị 5 lần mỗi nhánh, nhánh không tồn
    /// tại ≥ 50% nhánh sai mật khẩu — bỏ BCrypt giả thì tỉ lệ rơi xuống vài phần trăm.
    /// </summary>
    [Fact]
    public async Task AC02c_email_khong_ton_tai_ton_thoi_gian_it_nhat_mot_nua_nhanh_sai_mat_khau()
    {
        var (_, email) = await VerifiedUserAsync();
        (await _auth.PostLoginAsync(AuthTestClient.NewEmail(), WrongPassword)).Dispose();   // khởi tạo hash giả, không đo

        var wrong = new List<double>();
        var unknown = new List<double>();
        for (var i = 0; i < 5; i++)   // đúng 5 lần sai: lần thứ 5 khóa tài khoản nhưng vẫn trả 401
        {
            wrong.Add(await ElapsedMsAsync(email));
            unknown.Add(await ElapsedMsAsync(AuthTestClient.NewEmail()));
        }

        var ratio = Median(unknown) / Median(wrong);
        Assert.True(ratio >= 0.5,
            $"Email không tồn tại {Median(unknown):F0} ms, sai mật khẩu {Median(wrong):F0} ms (tỉ lệ {ratio:F2}) — BCrypt giả bị bỏ?");
    }

    [Fact]
    public async Task AC03_sai_5_lan_lien_tiep_deu_401_lan_6_dung_mat_khau_423()
    {
        var (userId, email) = await VerifiedUserAsync();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failed = await _auth.PostLoginAsync(email, WrongPassword);
            Assert.True(failed.StatusCode == HttpStatusCode.Unauthorized, $"lần sai {attempt}: nhận {(int)failed.StatusCode}");
        }

        using var sixth = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.Locked, sixth.StatusCode);
        await ReadProblemAsync(sixth);
        var row = await _auth.QueryRowAsync("SELECT failed_login_count, locked_until FROM identity.users WHERE user_id = $1", userId);
        Assert.Equal((short)0, row!["failed_login_count"]);
        var lockedUntil = new DateTimeOffset((DateTime)row["locked_until"]!);
        Assert.InRange(lockedUntil, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task AC03b_het_thoi_gian_khoa_dang_nhap_dung_200_va_trang_thai_sach()
    {
        var (userId, email) = await VerifiedUserAsync();
        for (var attempt = 0; attempt < 5; attempt++)
            (await _auth.PostLoginAsync(email, WrongPassword)).Dispose();
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.users SET locked_until = now() - interval '1 second' WHERE user_id = $1", userId);

        using var response = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await _auth.QueryRowAsync("SELECT failed_login_count, locked_until FROM identity.users WHERE user_id = $1", userId);
        Assert.Equal((short)0, row!["failed_login_count"]);
        Assert.Null(row["locked_until"]);
    }

    /// <summary>
    /// Mục 7.2 "Concurrency" qua HTTP: ĐÚNG 5 request sai song song. Với UPDATE nguyên tử cả 5 lần đều được đếm nên lần đăng
    /// nhập đúng ngay sau chắc chắn 423; đọc-rồi-ghi mà mất dù MỘT lần đếm là tài khoản không bị khóa. Bản đầu dùng 10
    /// request và thử cho đỏ vẫn xanh — 5 lần thừa đủ bù các lần đếm bị mất (thi công D3).
    /// Cả 5 phải là 401: tài khoản chỉ khóa ở lần đếm thứ 5, nên không request nào kịp thấy trạng thái khóa.
    /// </summary>
    [Fact]
    public async Task AC03c_dung_5_request_sai_song_song_khoa_tai_khoan()
    {
        var (_, email) = await VerifiedUserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _auth.PostLoginAsync(email, WrongPassword)));
        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        using var after = await _auth.PostLoginAsync(email, AuthTestClient.Password);
        Assert.Equal(HttpStatusCode.Locked, after.StatusCode);
    }

    /// <summary>
    /// Cùng lưới ở tầng store, KHÔNG có BCrypt đệm giữa các lời gọi: 5 lời gọi bắt đầu cùng lúc nên cửa sổ tranh chấp lớn
    /// nhất — đọc-rồi-ghi gần như chắc chắn đếm sót. Mỗi lời gọi một scope DI (một DbContext), như 5 request thật.
    /// </summary>
    [Fact]
    public async Task AC03d_RegisterFailedLogin_5_loi_goi_dong_thoi_deu_duoc_dem()
    {
        var (userId, _) = await VerifiedUserAsync();
        var now = DateTimeOffset.UtcNow;

        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IIdentityUserStore>()
                .RegisterFailedLoginAsync(userId, now, CancellationToken.None);
        }));

        var row = await _auth.QueryRowAsync("SELECT failed_login_count, locked_until FROM identity.users WHERE user_id = $1", userId);
        Assert.Equal((short)0, row!["failed_login_count"]);   // đủ 5 → khóa và reset bộ đếm
        Assert.NotNull(row["locked_until"]);
    }

    [Fact]
    public async Task AC04_chua_xac_minh_dung_mat_khau_403_khong_phat_token()
    {
        var (userId, email) = await UnverifiedUserAsync();

        using var response = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ReadProblemAsync(response);
        Assert.Null(AuthTestClient.ReadSetCookie(response));
        var count = await _auth.QueryRowAsync("SELECT count(*) AS n FROM identity.refresh_tokens WHERE user_id = $1", userId);
        Assert.Equal(0L, count!["n"]);
    }

    /// <summary>Bước 4 (mật khẩu) đứng trước bước 5 (xác minh): không cần mật khẩu thì không dò được email chưa xác minh.</summary>
    [Fact]
    public async Task AC04b_chua_xac_minh_sai_mat_khau_401()
    {
        var (_, email) = await UnverifiedUserAsync();

        using var response = await _auth.PostLoginAsync(email, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dang_nhap_dung_sau_3_lan_sai_bo_dem_ve_0()
    {
        var (userId, email) = await VerifiedUserAsync();
        for (var attempt = 0; attempt < 3; attempt++)
            (await _auth.PostLoginAsync(email, WrongPassword)).Dispose();

        using var response = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await _auth.QueryRowAsync("SELECT failed_login_count FROM identity.users WHERE user_id = $1", userId);
        Assert.Equal((short)0, row!["failed_login_count"]);
    }

    [Fact]
    public async Task Hai_lan_dang_nhap_hai_family_id_khac_nhau()
    {
        var (userId, email) = await VerifiedUserAsync();

        await _auth.LoginAsync(email, AuthTestClient.Password);
        await _auth.LoginAsync(email, AuthTestClient.Password);

        var row = await _auth.QueryRowAsync(
            "SELECT count(*) AS tokens, count(DISTINCT family_id) AS families FROM identity.refresh_tokens WHERE user_id = $1", userId);
        Assert.Equal(2L, row!["tokens"]);
        Assert.Equal(2L, row["families"]);
    }

    public static TheoryData<string, string, string> InvalidInputs => new()
    {
        { "", AuthTestClient.Password, "email" },
        { "an@example.com", "", "password" },
        { "an@example.com", new string('a', 73), "password" },   // 73 byte — trần BCrypt
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Du_lieu_sai_400_errors_camelCase(string email, string password, string field)
    {
        using var response = await _auth.PostLoginAsync(email, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"errors không có key '{field}': {problem}");
    }

    private async Task<(Guid UserId, string Email)> VerifiedUserAsync()
    {
        var email = AuthTestClient.NewEmail();
        return (await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password), email);
    }

    private async Task<(Guid UserId, string Email)> UnverifiedUserAsync()
    {
        var email = AuthTestClient.NewEmail();
        using var response = await _auth.RegisterAsync(email, AuthTestClient.Password);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("userId").GetGuid(), email);
    }

    private async Task<double> ElapsedMsAsync(string email)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await _auth.PostLoginAsync(email, WrongPassword);
        stopwatch.Stop();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    private static string Sha256Hex(string plain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();

    private static async Task<string> BodyWithoutPerRequestFieldsAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    private static string HeadersWithoutCorrelationId(HttpResponseMessage response) =>
        string.Join("\n", response.Headers.Concat(response.Content.Headers)
            .Where(h => !h.Key.Equals("X-Correlation-ID", StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }
}
