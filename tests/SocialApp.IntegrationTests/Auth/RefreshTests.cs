using System.Net;
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
/// D5 — <c>POST /auth/refresh</c>: rotation + reuse detection + ân hạn 10 giây (NFR-SEC-03, giai-doan-1.md Mục 7.3, Đ-D3) trên
/// Postgres thật. "Quá ân hạn" và "hết hạn" tạo bằng cách lùi mốc trong DB (Đ-D10). Reuse detection kiểm bằng HỆ QUẢ (token
/// kế nhiệm cũng chết, DB mọi dòng của family có revoked_at), không chỉ status code: nhánh reuse mà rollback thì vẫn 401.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RefreshTests(PostgresFixture postgres, IdentityApiFactory factory)
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
    public async Task RT01_refresh_200_access_moi_dung_duoc_cookie_moi_token_cu_bi_xoay_cung_family()
    {
        var (_, first) = await LoggedInAsync();

        using var response = await _auth.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        using var me = await _auth.GetMeAsync(body.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var second = RotatedCookie(response);
        Assert.NotEqual(first, second);

        var old = await RowAsync(first);
        var next = await RowAsync(second);
        Assert.NotNull(old["revoked_at"]);
        Assert.Equal(next["id"], old["replaced_by_id"]);
        Assert.Equal(old["family_id"], next["family_id"]);
        Assert.Null(next["revoked_at"]);
        Assert.Null(next["replaced_by_id"]);
    }

    [Fact]
    public async Task RT01b_chuoi_xoay_tiep_duoc_nhieu_lan()
    {
        var (_, cookie) = await LoggedInAsync();

        for (var round = 1; round <= 3; round++)
        {
            using var response = await _auth.RefreshAsync(cookie);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"lượt {round}: nhận {(int)response.StatusCode}");
            cookie = RotatedCookie(response);
        }
    }

    /// <summary>Reuse ngoài ân hạn → 401 + xóa cookie, VÀ token kế nhiệm cũng chết — bằng chứng family bị thu hồi thật (đã COMMIT).</summary>
    [Fact]
    public async Task RT02_dung_lai_token_cu_sau_11_giay_401_va_token_ke_nhiem_cung_chet()
    {
        var (_, first) = await LoggedInAsync();
        var second = await RefreshOkAsync(first);
        await ShiftRevokedAtAsync(first, seconds: 11);

        using var reused = await _auth.RefreshAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        AssertCookieCleared(reused);

        using var successor = await _auth.RefreshAsync(second);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);

        var family = await FamilyAsync(first);
        Assert.Equal(0L, family["live"]);
    }

    /// <summary>Token hết hạn KHÔNG phải reuse: 401 nhưng family giữ nguyên.</summary>
    [Fact]
    public async Task RT03_token_het_han_401_va_family_khong_bi_thu_hoi()
    {
        var (_, cookie) = await LoggedInAsync();
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.refresh_tokens SET expires_at = now() - interval '1 second' WHERE token_hash = $1", Sha256Hex(cookie));

        using var response = await _auth.RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertCookieCleared(response);
        var row = await RowAsync(cookie);
        Assert.Null(row["revoked_at"]);
        Assert.Null(row["replaced_by_id"]);
    }

    /// <summary>
    /// Race hai tab — test quan trọng nhất của khối. Hai request cùng token: request thứ hai chờ ở FOR UPDATE, đọc lại dòng đã
    /// bị xoay và rơi vào ân hạn. Cả hai 200, hai cookie khác nhau, cả hai refresh tiếp được.
    /// </summary>
    [Fact]
    public async Task RT04_hai_refresh_song_song_cung_token_ca_hai_200_va_ca_hai_xoay_tiep_duoc()
    {
        var (_, first) = await LoggedInAsync();

        var responses = await Task.WhenAll(_auth.RefreshAsync(first), _auth.RefreshAsync(first));
        string cookieA, cookieB;
        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            cookieA = RotatedCookie(responses[0]);
            cookieB = RotatedCookie(responses[1]);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
        Assert.NotEqual(cookieA, cookieB);

        // Đúng MỘT dòng bị xoay (token gốc); family có 3 dòng, 2 còn sống. Không FOR UPDATE thì cả hai cùng xoay.
        var family = await FamilyAsync(first);
        Assert.Equal(3L, family["total"]);
        Assert.Equal(2L, family["live"]);
        Assert.Equal(1L, family["replaced"]);

        await RefreshOkAsync(cookieA);
        await RefreshOkAsync(cookieB);
    }

    [Fact]
    public async Task RT05_dung_lai_sau_11_giay_la_ngoai_an_han_ca_family_bi_thu_hoi()
    {
        var (_, first) = await LoggedInAsync();
        await RefreshOkAsync(first);
        await ShiftRevokedAtAsync(first, seconds: 11);

        using var response = await _auth.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var family = await FamilyAsync(first);
        Assert.Equal(2L, family["total"]);
        Assert.Equal(0L, family["live"]);
    }

    /// <summary>Biên còn lại của ân hạn: 9 giây vẫn phát token mới cùng family, không thu hồi gì.</summary>
    [Fact]
    public async Task RT05b_dung_lai_sau_9_giay_van_trong_an_han_200()
    {
        var (_, first) = await LoggedInAsync();
        await RefreshOkAsync(first);
        await ShiftRevokedAtAsync(first, seconds: 9);

        using var response = await _auth.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var family = await FamilyAsync(first);
        Assert.Equal(3L, family["total"]);
        Assert.Equal(2L, family["live"]);
    }

    /// <summary>
    /// Khe hở tìm ra ở thi công D5: reuse detection của token cũ T1 (quá ân hạn) chạy SONG SONG với lượt xoay hợp lệ của token kế
    /// nhiệm T2. Trigger chỉ tồn tại trong database test giữ lượt xoay T2 lại ngay sau khi INSERT T3 — lúc đó dòng T2 đang bị
    /// khóa và T3 chưa commit — rồi lượt reuse chen vào đúng khoảnh khắc đó. Không khóa theo family thì câu UPDATE thu hồi family
    /// chờ khóa dòng T2, và T3 (commit sau khi câu lệnh đã lấy snapshot) sống sót dù family bị coi là đã thu hồi.
    /// Gọi thẳng store (mỗi lời gọi một scope DI như một request) để điều khiển được thứ tự.
    /// </summary>
    [Fact]
    public async Task RT06_reuse_chen_vao_giua_luot_xoay_token_ke_nhiem_van_thu_hoi_ca_token_vua_sinh()
    {
        var (_, first) = await LoggedInAsync();
        var second = await RefreshOkAsync(first);
        await ShiftRevokedAtAsync(first, seconds: 11);
        var familyId = (Guid)(await RowAsync(first))["family_id"]!;

        await using (await RefreshFamilyRace.HoldInsertsIntoFamilyAsync(_auth, familyId))
        {
            var rotation = RotateThroughStoreAsync(second);   // khóa dòng T2, INSERT T3 rồi ngủ trong trigger
            await RefreshFamilyRace.WaitUntilSessionSleepsAsync(_auth);
            var reuse = RotateThroughStoreAsync(first);        // chen vào khi T3 chưa commit

            var outcomes = await Task.WhenAll(rotation, reuse);

            Assert.IsType<RotateOutcome.Rotated>(outcomes[0]);
            Assert.IsType<RotateOutcome.ReuseDetected>(outcomes[1]);
        }

        var family = await FamilyAsync(first);
        Assert.Equal(3L, family["total"]);
        Assert.True((long)family["live"]! == 0, $"reuse detection bỏ sót {family["live"]} token còn sống trong family");
    }

    /// <summary>Không cookie, cookie rác, cookie hết hạn, cookie bị dùng lại: CÙNG một 401 (bỏ traceId/instance) và đều xóa cookie.</summary>
    [Fact]
    public async Task Moi_nhanh_hong_cung_mot_401_va_deu_xoa_cookie()
    {
        var (_, expired) = await LoggedInAsync();
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.refresh_tokens SET expires_at = now() - interval '1 second' WHERE token_hash = $1", Sha256Hex(expired));

        var (_, reused) = await LoggedInAsync();
        await RefreshOkAsync(reused);
        await ShiftRevokedAtAsync(reused, seconds: 11);

        var cookies = new (string Case, string? Value)[]
        {
            ("không cookie", null),
            ("64 hex không tồn tại", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()),
            ("chuỗi rác", "abc"),
            ("hết hạn", expired),
            ("dùng lại ngoài ân hạn", reused),
        };

        var bodies = new List<string>();
        foreach (var (name, value) in cookies)
        {
            using var response = await _auth.RefreshAsync(value);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: nhận {(int)response.StatusCode}");
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            AssertCookieCleared(response);
            bodies.Add(await BodyWithoutPerRequestFieldsAsync(response));
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal("Phiên không hợp lệ", JsonNode.Parse(bodies[0])!["title"]?.GetValue<string>());
    }

    /// <summary>Endpoint không nhận body (hợp đồng): body lạ đi kèm cookie hợp lệ không gây 400.</summary>
    [Fact]
    public async Task Body_la_kem_cookie_hop_le_van_200()
    {
        var (_, cookie) = await LoggedInAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken = "khong-duoc-doc" }),
        };
        request.Headers.Add("Cookie", $"{AuthTestClient.RefreshCookieName}={cookie}");
        using var response = await _auth.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Cạm bẫy 7: vai trò của access token mới đọc từ DB — hạ/nâng quyền ở GĐ6 có hiệu lực sau một lần refresh.</summary>
    [Fact]
    public async Task Access_token_moi_mang_vai_tro_doc_tu_DB_khong_chep_tu_token_cu()
    {
        var (userId, cookie) = await LoggedInAsync();
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.users SET role_id = (SELECT role_id FROM identity.roles WHERE code = 'MODERATOR') WHERE user_id = $1",
            userId);

        using var response = await _auth.RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accessToken = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        Assert.Equal("MODERATOR", new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetPayloadValue<string>("role"));
    }

    private async Task<(Guid UserId, string RefreshCookie)> LoggedInAsync()
    {
        var email = AuthTestClient.NewEmail();
        var userId = await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);
        var (_, refreshCookie) = await _auth.LoginAsync(email, AuthTestClient.Password);
        return (userId, refreshCookie);
    }

    private async Task<RotateOutcome> RotateThroughStoreAsync(string cookie)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var now = DateTimeOffset.UtcNow;
        return await scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>().RotateAsync(
            Sha256Hex(cookie), now, Sha256Hex(Guid.NewGuid().ToString("N")), now.AddDays(7), createdIp: null, CancellationToken.None);
    }

    private async Task<string> RefreshOkAsync(string cookie)
    {
        using var response = await _auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return RotatedCookie(response);
    }

    /// <summary>Cookie mới trong response 200: 64 hex, Max-Age 7 ngày.</summary>
    private static string RotatedCookie(HttpResponseMessage response)
    {
        var cookie = AuthTestClient.ReadSetCookie(response);
        Assert.NotNull(cookie);
        Assert.Equal(TimeSpan.FromSeconds(604800), cookie.MaxAge);
        var value = cookie.Value.ToString();
        Assert.Matches("^[0-9a-f]{64}$", value);
        return value;
    }

    private static void AssertCookieCleared(HttpResponseMessage response)
    {
        var cookie = AuthTestClient.ReadSetCookie(response);
        Assert.NotNull(cookie);
        Assert.Equal("", cookie.Value.ToString());
        Assert.Equal(TimeSpan.Zero, cookie.MaxAge);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
    }

    private Task<int> ShiftRevokedAtAsync(string cookie, int seconds) =>
        _auth.ExecuteSqlAsync(
            $"UPDATE identity.refresh_tokens SET revoked_at = revoked_at - interval '{seconds} seconds' WHERE token_hash = $1",
            Sha256Hex(cookie));

    private async Task<IReadOnlyDictionary<string, object?>> RowAsync(string cookie) =>
        await _auth.QueryRowAsync("SELECT * FROM identity.refresh_tokens WHERE token_hash = $1", Sha256Hex(cookie))
        ?? throw new InvalidOperationException("Không có dòng refresh_tokens cho cookie này.");

    private async Task<IReadOnlyDictionary<string, object?>> FamilyAsync(string anyCookieInFamily)
    {
        var familyId = (Guid)(await RowAsync(anyCookieInFamily))["family_id"]!;
        return (await _auth.QueryRowAsync("""
            SELECT count(*) AS total,
                   count(*) FILTER (WHERE revoked_at IS NULL) AS live,
                   count(replaced_by_id) AS replaced
              FROM identity.refresh_tokens WHERE family_id = $1
            """, familyId))!;
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
}
