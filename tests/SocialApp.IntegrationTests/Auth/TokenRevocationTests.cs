using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog.Core;
using Serilog.Events;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D8 — thu hồi access token theo user + <c>iat</c> (giai-doan-1.md Mục 7.5, RV-01 → RV-04) trên Postgres + Redis thật.
///
/// RV-01/02 ký token bằng <see cref="TestJwt"/> (cần <c>iat</c> tùy ý) nên <c>sub</c> không có trong DB → KHÔNG gọi <c>/me</c> (trả
/// 401 cho user không tồn tại dù token không bị thu hồi). Gọi route không tồn tại: token qua được tầng 1 → 404, bị thu hồi → 401
/// (fallback policy) — cùng lưới <see cref="AuthHarnessTests"/>. Key <c>revoked:user:</c> ghi bằng chuỗi hợp đồng viết tay.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TokenRevocationTests(PostgresFixture postgres, RedisFixture redis, IdentityApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string MissingRoute = "/api/v1/__khong-ton-tai";

    private AuthTestClient _auth = null!;

    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        _auth = new AuthTestClient(factory);

        // App bắt đầu kết nối Redis lúc khởi động nhưng KHÔNG chờ; request đến trong vài ms đầu vẫn có thể fail-open → token bị thu
        // hồi 404, test đỏ ngẫu nhiên. Bên ghi thì CHỜ kết nối: ghi một key rác qua store của app là đủ để bên đọc chắc chắn thấy Redis.
        await factory.Services.GetRequiredService<ITokenRevocationStore>().RevokeUserAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RV01_token_phat_truoc_moc_thu_hoi_401_user_khac_khong_bi_anh_huong()
    {
        var userId = Guid.NewGuid();
        var revokedAt = await RevokeByHandAsync(userId);

        using var revoked = await GetWithTokenAsync(TestJwt.Create("USER", userId, issuedAt: revokedAt.AddSeconds(-60)));
        using var otherUser = await GetWithTokenAsync(TestJwt.Create("USER", Guid.NewGuid(), issuedAt: revokedAt.AddSeconds(-60)));

        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("application/problem+json", revoked.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, otherUser.StatusCode);
    }

    /// <summary>So CHẶT: token phát ĐÚNG giây thu hồi (vd đăng nhập lại ngay sau khi bị thu hồi) vẫn qua — cạm bẫy 6.</summary>
    [Fact]
    public async Task RV02_token_phat_bang_hoac_sau_moc_thu_hoi_van_qua_tang_1()
    {
        var userId = Guid.NewGuid();
        var revokedAt = await RevokeByHandAsync(userId);

        using var sameSecond = await GetWithTokenAsync(TestJwt.Create("USER", userId, issuedAt: revokedAt));
        using var after = await GetWithTokenAsync(TestJwt.Create("USER", userId, issuedAt: revokedAt.AddSeconds(5)));

        Assert.Equal(HttpStatusCode.NotFound, sameSecond.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    /// <summary>
    /// Reuse detection (D5) thu hồi CẢ access token: access của lần đăng nhập đầu đang dùng được thì chết ngay, không đợi hết 15
    /// phút. Đăng nhập lại sau đó dùng được — key chặn token CŨ, không chặn user.
    /// </summary>
    [Fact]
    public async Task RV03_reuse_detection_thu_hoi_access_token_dang_song_dang_nhap_lai_van_dung_duoc()
    {
        var session = await NewSessionAsync();
        using (var before = await _auth.GetMeAsync(session.AccessToken))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await TriggerReuseDetectionAsync(session);

        using (var after = await _auth.GetMeAsync(session.AccessToken))
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        var stored = await redis.Database.StringGetAsync($"revoked:user:{session.UserId}");
        Assert.True(stored.HasValue, "reuse detection không ghi revoked:user");
        Assert.True((long)stored > IssuedAt(session.AccessToken));

        var (again, _) = await _auth.LoginAsync(session.Email, AuthTestClient.Password);
        using var relogin = await _auth.GetMeAsync(again);
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    /// <summary>
    /// Đ-D4: key sống bằng TTL access + ClockSkew — ngắn hơn thì token phát ngay trước mốc sống lại ở những giây cuối. Kỳ vọng
    /// tính từ <see cref="JwtOptions"/> của app VÀ viết tay 930 (TestJwt đặt 900, ClockSkew 30).
    /// </summary>
    [Fact]
    public async Task Ttl_bang_access_cong_clock_skew()
    {
        var session = await NewSessionAsync();
        await TriggerReuseDetectionAsync(session);

        var ttl = await redis.Database.KeyTimeToLiveAsync($"revoked:user:{session.UserId}");

        var jwt = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var expected = jwt.AccessTokenSeconds + JwtOptions.ClockSkewSeconds;
        Assert.Equal(930, expected);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, expected - 5, expected);
    }

    /// <summary>Không có <c>iat</c> thì không so được với mốc thu hồi — cho qua là tạo ra token không bao giờ thu hồi được.</summary>
    [Fact]
    public async Task Token_khong_co_iat_401()
    {
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestJwt.Issuer,
            Audience = TestJwt.Audience,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["role"] = "USER" },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        Assert.False(new JsonWebTokenHandler().ReadJsonWebToken(token).TryGetPayloadValue<long>("iat", out _));

        using var response = await GetWithTokenAsync(token);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"token không có iat qua tầng 1: nhận {(int)response.StatusCode}");
    }

    /// <summary>
    /// Redis không tới được: token hợp lệ vẫn được phục vụ (fail-open), NHANH — không chờ ConnectTimeout 2 giây ở mỗi request — và
    /// có log warning. Dựng app riêng từ <see cref="ApiFactory"/> (Redis cổng 1, không chạm DB); request ẩn danh đầu tiên làm nóng
    /// app để thời gian đo chỉ còn phần tầng 1.
    /// </summary>
    [Fact]
    public async Task RV04_Redis_khong_toi_duoc_token_hop_le_van_qua_duoi_1_giay_va_log_warning()
    {
        var logs = new CapturingLogSink();
        await using var baseFactory = new ApiFactory();
        await using var app = baseFactory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(logs)));
        using var client = app.CreateClient();
        using (var warmUp = await client.GetAsync("/api/v1/ping"))
            Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MissingRoute);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("USER"));
            var elapsed = Stopwatch.StartNew();
            using var response = await client.SendAsync(request);
            elapsed.Stop();

            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"lần {attempt}: nhận {(int)response.StatusCode}");
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"lần {attempt}: {elapsed.ElapsedMilliseconds} ms");
        }

        Assert.Contains(logs.Events, e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.Contains("fail-open"));
    }

    /// <summary>
    /// Kết nối Redis mở lúc HOST KHỞI ĐỘNG, không đợi request có token đầu tiên: dựng lười thuần túy thì chính request đó fail-open và
    /// token đã bị thu hồi lọt qua (thi công D8). App mới dựng, không request nào chạm store trước request đo.
    /// </summary>
    [Fact]
    public async Task Request_co_token_dau_tien_sau_khoi_dong_van_duoc_kiem_thu_hoi()
    {
        var userId = Guid.NewGuid();
        var revokedAt = await RevokeByHandAsync(userId);
        var logs = new CapturingLogSink();
        await using var app = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(logs)));
        using var client = app.CreateClient();
        await WaitForLogAsync(logs, "Đã kết nối Redis");

        using var request = new HttpRequestMessage(HttpMethod.Get, MissingRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Create("USER", userId, issuedAt: revokedAt.AddSeconds(-60)));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(logs.Events, e => e.MessageTemplate.Text.Contains("fail-open"));
    }

    /// <summary>
    /// Health check và thu hồi token dùng CHUNG một kết nối: số client Redis mang tên của app không tăng sau khi gọi /health/ready và
    /// request có token. Tên client đặt qua chuỗi kết nối (<c>name=</c>), không cần hook trong <c>src/backend/</c>.
    /// </summary>
    [Fact]
    public async Task Health_ready_200_va_dung_chung_mot_ket_noi_Redis_voi_thu_hoi_token()
    {
        var clientName = $"d8share{Guid.NewGuid():N}";
        var logs = new CapturingLogSink();
        await using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Redis", $"{redis.ConnectionString},name={clientName}");
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(logs));
        });
        using var client = app.CreateClient();
        await WaitForLogAsync(logs, "Đã kết nối Redis");
        var afterStartup = await StableClientCountAsync(clientName);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var ready = await client.GetAsync("/health/ready");
            Assert.True(ready.StatusCode == HttpStatusCode.OK, $"lần {attempt}: /health/ready nhận {(int)ready.StatusCode}");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, MissingRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("USER"));
        using (var withToken = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);

        Assert.True(afterStartup > 0, "app không có kết nối Redis nào mang tên đã đặt");
        Assert.Equal(afterStartup, await StableClientCountAsync(clientName));
    }

    private static async Task WaitForLogAsync(CapturingLogSink logs, string text)
    {
        var elapsed = Stopwatch.StartNew();
        while (!logs.Events.Any(e => e.MessageTemplate.Text.Contains(text)))
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10))
                Assert.Fail($"10 giây sau khởi động app vẫn chưa log \"{text}\" — kết nối Redis không được mở lúc host khởi động");
            await Task.Delay(50);
        }
    }

    /// <summary>Đếm client theo tên trong <c>CLIENT LIST</c>, đọc tới khi hai lần liên tiếp (cách 200 ms) bằng nhau.</summary>
    private async Task<int> StableClientCountAsync(string clientName)
    {
        var previous = -1;
        for (var read = 0; read < 25; read++)
        {
            var list = (string)(await redis.Database.ExecuteAsync("CLIENT", "LIST"))!;
            var count = list.Split('\n').Count(line => line.Contains($" name={clientName} "));
            if (count == previous)
                return count;
            previous = count;
            await Task.Delay(200);
        }
        return previous;
    }

    private sealed record Session(string Email, Guid UserId, string AccessToken, string RefreshCookie);

    private async Task<Session> NewSessionAsync()
    {
        var email = AuthTestClient.NewEmail();
        var userId = await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);
        var (access, cookie) = await _auth.LoginAsync(email, AuthTestClient.Password);
        return new Session(email, userId, access, cookie);
    }

    /// <summary>
    /// Refresh (T1 → T2), lùi revoked_at của T1 quá ân hạn, dùng lại T1 → 401. Trước đó chờ sang giây KẾ TIẾP của <c>iat</c> của
    /// access token: mốc thu hồi làm tròn xuống giây và so chặt, nên reuse xảy ra cùng giây đăng nhập thì token đó (đúng thiết kế)
    /// không bị chặn — test sẽ đỏ ngẫu nhiên.
    /// </summary>
    private async Task TriggerReuseDetectionAsync(Session session)
    {
        using (var rotated = await _auth.RefreshAsync(session.RefreshCookie))
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.refresh_tokens SET revoked_at = revoked_at - interval '11 seconds' WHERE token_hash = $1",
            Sha256Hex(session.RefreshCookie));

        var iat = IssuedAt(session.AccessToken);
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= iat)
            await Task.Delay(50);

        using var reused = await _auth.RefreshAsync(session.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    /// <summary>Ghi key bằng tay, không qua store của app. Mốc là giây tròn trong quá khứ để <c>iat</c> so đúng bằng được.</summary>
    private async Task<DateTimeOffset> RevokeByHandAsync(Guid userId)
    {
        var revokedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100);
        await redis.Database.StringSetAsync($"revoked:user:{userId}", revokedAt.ToUnixTimeSeconds(), TimeSpan.FromMinutes(5));
        return revokedAt;
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MissingRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _auth.Http.SendAsync(request);
    }

    private static long IssuedAt(string accessToken) =>
        new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetPayloadValue<long>("iat");

    private static string Sha256Hex(string plain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
}
