using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D6 — <c>POST /auth/logout</c>: thu hồi toàn bộ family của THIẾT BỊ đó (báo cáo Mục 6.7.3, giai-doan-1.md Mục 7.4), có kiểm
/// chủ sở hữu (tầng 3, Đ-D6). Logout idempotent: cookie thiếu/lạ/của người khác vẫn 204 + xóa cookie, không thu hồi gì. Kiểm
/// bằng HỆ QUẢ qua refresh và DB, không chỉ status code.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LogoutTests(PostgresFixture postgres, IdentityApiFactory factory)
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
    public async Task Logout_204_va_xoa_cookie_cung_path()
    {
        var session = await NewSessionAsync();

        using var response = await _auth.LogoutAsync(session.AccessToken, session.RefreshCookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task Sau_logout_refresh_bang_cookie_cu_401_va_family_khong_con_la_song()
    {
        var session = await NewSessionAsync();

        using (var logout = await _auth.LogoutAsync(session.AccessToken, session.RefreshCookie))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var refresh = await _auth.RefreshAsync(session.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(0L, (await FamilyAsync(session.RefreshCookie))["live"]);
    }

    /// <summary>
    /// Logout bằng T2 thu hồi cả family, nên T1 (đã xoay, vẫn trong 10 giây) không còn lá sống để hưởng ân hạn → rơi vào nhánh
    /// reuse: token cũ bị dùng lại sau khi đăng xuất là đáng nghi.
    /// </summary>
    [Fact]
    public async Task Logout_bang_T2_roi_dung_lai_T1_trong_10_giay_van_401()
    {
        var session = await NewSessionAsync();
        string second;
        using (var refresh = await _auth.RefreshAsync(session.RefreshCookie))
        {
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            second = AuthTestClient.ReadSetCookie(refresh)!.Value.ToString();
        }

        using (var logout = await _auth.LogoutAsync(session.AccessToken, second))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var reused = await _auth.RefreshAsync(session.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        var family = await FamilyAsync(session.RefreshCookie);
        Assert.Equal(2L, family["total"]);
        Assert.Equal(0L, family["live"]);
    }

    /// <summary>Bảng Mục 7.5: đăng xuất một thiết bị → CHỈ family đó. Hai lần đăng nhập = hai thiết bị.</summary>
    [Fact]
    public async Task Logout_thiet_bi_A_khong_anh_huong_thiet_bi_B_cung_tai_khoan()
    {
        var deviceA = await NewSessionAsync();
        var (_, cookieB) = await _auth.LoginAsync(deviceA.Email, AuthTestClient.Password);

        using (var logout = await _auth.LogoutAsync(deviceA.AccessToken, deviceA.RefreshCookie))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var refreshB = await _auth.RefreshAsync(cookieB);
        Assert.Equal(HttpStatusCode.OK, refreshB.StatusCode);
    }

    /// <summary>Bắt [AllowAnonymous] lọt lên class: logout cần bearer.</summary>
    [Fact]
    public async Task Khong_bearer_co_cookie_401_va_khong_thu_hoi_gi()
    {
        var session = await NewSessionAsync();

        using var response = await _auth.LogoutAsync(accessToken: null, session.RefreshCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1L, (await FamilyAsync(session.RefreshCookie))["live"]);
    }

    /// <summary>Đ-D6: logout idempotent — không cookie vẫn 204.</summary>
    [Fact]
    public async Task Bearer_hop_le_khong_cookie_204_va_xoa_cookie()
    {
        var session = await NewSessionAsync();

        using var response = await _auth.LogoutAsync(session.AccessToken, refreshCookie: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCookieCleared(response);
        Assert.Equal(1L, (await FamilyAsync(session.RefreshCookie))["live"]);
    }

    /// <summary>
    /// Tầng 3 (Đ-D6): bearer của A + cookie của B (máy dùng chung) → 204 nhưng family của B KHÔNG bị thu hồi. Thiếu điều kiện chủ
    /// sở hữu là ai cầm cookie của người khác cũng đăng xuất được họ. AuthZ matrix không mang cookie nên tầng 3 kiểm ở đây.
    /// </summary>
    [Fact]
    public async Task Cookie_cua_nguoi_khac_khong_bi_thu_hoi()
    {
        var userA = await NewSessionAsync();
        var userB = await NewSessionAsync();

        using (var logout = await _auth.LogoutAsync(userA.AccessToken, userB.RefreshCookie))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            AssertCookieCleared(logout);
        }

        Assert.Equal(1L, (await FamilyAsync(userB.RefreshCookie))["live"]);
        using var refreshB = await _auth.RefreshAsync(userB.RefreshCookie);
        Assert.Equal(HttpStatusCode.OK, refreshB.StatusCode);
    }

    /// <summary>
    /// Đúng thiết kế (hợp đồng ghi rõ): access token đang cầm vẫn sống tới hết hạn — logout KHÔNG ghi revoked:user, vì key đó cắt
    /// access token của MỌI thiết bị (trái bảng Mục 7.5). Client tự xóa access token khỏi memory.
    /// </summary>
    [Fact]
    public async Task Access_token_van_dung_duoc_sau_logout()
    {
        var session = await NewSessionAsync();

        using (var logout = await _auth.LogoutAsync(session.AccessToken, session.RefreshCookie))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var me = await _auth.GetMeAsync(session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    /// <summary>
    /// Cạm bẫy 8 của D5 áp cho logout: lượt xoay T1 đang giữ khóa dòng, vừa INSERT T2 chưa commit; logout bằng T1 chen vào. Không
    /// khóa theo family thì câu UPDATE của logout chờ khóa dòng T1, còn T2 (commit sau khi câu lệnh lấy snapshot) sống sót — người
    /// dùng tưởng đã đăng xuất mà phiên vẫn refresh được. Gọi thẳng store để điều khiển thứ tự.
    /// </summary>
    [Fact]
    public async Task Logout_chen_vao_giua_luot_xoay_van_thu_hoi_token_vua_sinh()
    {
        var session = await NewSessionAsync();
        var familyId = (Guid)(await RowAsync(session.RefreshCookie))["family_id"]!;

        int revoked;
        await using (await RefreshFamilyRace.HoldInsertsIntoFamilyAsync(_auth, familyId))
        {
            var rotation = WithStoreAsync(store =>
            {
                var now = DateTimeOffset.UtcNow;
                return store.RotateAsync(Sha256Hex(session.RefreshCookie), now, Sha256Hex(Guid.NewGuid().ToString("N")),
                    now.AddDays(7), createdIp: null, CancellationToken.None);
            });
            await RefreshFamilyRace.WaitUntilSessionSleepsAsync(_auth);
            var logout = WithStoreAsync(store =>
                store.RevokeFamilyAsync(Sha256Hex(session.RefreshCookie), session.UserId, DateTimeOffset.UtcNow, CancellationToken.None));

            Assert.IsType<RotateOutcome.Rotated>(await rotation);
            revoked = await logout;
        }

        var family = await FamilyAsync(session.RefreshCookie);
        Assert.Equal(2L, family["total"]);
        Assert.True((long)family["live"]! == 0, $"logout bỏ sót {family["live"]} token còn sống (thu hồi được {revoked})");
    }

    /// <summary>
    /// Cạm bẫy "thu hồi chỉ một dòng": hai refresh song song cùng token (ân hạn, RT-04) để lại HAI lá sống trong family. Logout
    /// bằng một lá phải làm chết cả lá kia — thu hồi theo token_hash thay vì family_id thì lá thứ hai vẫn refresh được sau logout.
    /// </summary>
    [Fact]
    public async Task Logout_khi_family_co_hai_la_song_sau_an_han_thu_hoi_ca_hai()
    {
        var session = await NewSessionAsync();

        var responses = await Task.WhenAll(_auth.RefreshAsync(session.RefreshCookie), _auth.RefreshAsync(session.RefreshCookie));
        string leafA, leafB;
        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            leafA = AuthTestClient.ReadSetCookie(responses[0])!.Value.ToString();
            leafB = AuthTestClient.ReadSetCookie(responses[1])!.Value.ToString();
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        using (var logout = await _auth.LogoutAsync(session.AccessToken, leafA))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var refreshB = await _auth.RefreshAsync(leafB);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshB.StatusCode);
        Assert.Equal(0L, (await FamilyAsync(session.RefreshCookie))["live"]);
    }

    private sealed record Session(Guid UserId, string Email, string AccessToken, string RefreshCookie);

    private async Task<Session> NewSessionAsync()
    {
        var email = AuthTestClient.NewEmail();
        var userId = await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);
        var (accessToken, refreshCookie) = await _auth.LoginAsync(email, AuthTestClient.Password);
        return new Session(userId, email, accessToken, refreshCookie);
    }

    private async Task<T> WithStoreAsync<T>(Func<IRefreshTokenStore, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>());
    }

    private static void AssertCookieCleared(HttpResponseMessage response)
    {
        var cookie = AuthTestClient.ReadSetCookie(response);
        Assert.NotNull(cookie);
        Assert.Equal("", cookie.Value.ToString());
        Assert.Equal(TimeSpan.Zero, cookie.MaxAge);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
    }

    private async Task<IReadOnlyDictionary<string, object?>> RowAsync(string cookie) =>
        await _auth.QueryRowAsync("SELECT * FROM identity.refresh_tokens WHERE token_hash = $1", Sha256Hex(cookie))
        ?? throw new InvalidOperationException("Không có dòng refresh_tokens cho cookie này.");

    private async Task<IReadOnlyDictionary<string, object?>> FamilyAsync(string anyCookieInFamily)
    {
        var familyId = (Guid)(await RowAsync(anyCookieInFamily))["family_id"]!;
        return (await _auth.QueryRowAsync("""
            SELECT count(*) AS total, count(*) FILTER (WHERE revoked_at IS NULL) AS live
              FROM identity.refresh_tokens WHERE family_id = $1
            """, familyId))!;
    }

    private static string Sha256Hex(string plain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
}
