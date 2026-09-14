using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application.Email;
using SocialApp.Modules.Identity.Application.Security;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// Harness auth tự nó đúng: database riêng đã migrate + seed, sender giả đã thay, parser Set-Cookie. Hai test "register
/// chưa có controller" của D0 đã gỡ ở D1 — từ đó <see cref="RegisterTests"/> là bằng chứng harness đi hết đường HTTP.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthHarnessTests(PostgresFixture postgres, IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Token phát bằng <see cref="IAccessTokenIssuer"/> lấy từ DI của app, validate bằng JwtBearer THẬT của Program.cs.
    /// Route cố ý không bao giờ tồn tại: có token hợp lệ → 404; lệch khóa/issuer/audience giữa hai phía → fallback policy
    /// trả 401. Không token → 401, chứng minh 404 kia đến từ tầng 1 chứ không phải route bị bỏ qua xác thực.
    /// </summary>
    [Fact]
    public async Task Token_cua_app_qua_JwtBearer_that_route_khong_ton_tai_404_an_danh_401()
    {
        const string path = "/api/v1/__khong-ton-tai";
        var auth = new AuthTestClient(factory);
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Guid.NewGuid(), "USER");

        using var authenticated = new HttpRequestMessage(HttpMethod.Get, path);
        authenticated.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var withToken = await auth.Http.SendAsync(authenticated);
        using var anonymous = await auth.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Database_rieng_da_migrate_va_seed_ExecuteSql_nhan_tham_so()
    {
        var auth = new AuthTestClient(factory);

        var rows = await auth.ExecuteSqlAsync("UPDATE identity.roles SET display_name = display_name WHERE code = $1", "USER");

        Assert.Equal(1, rows);
    }

    [Fact]
    public void IEmailSender_cua_app_la_CapturingEmailSender()
    {
        Assert.Same(factory.Emails, factory.Services.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void ReadSetCookie_khong_phan_biet_hoa_thuong_va_chon_dung_ten()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", "other=x; path=/");
        response.Headers.TryAddWithoutValidation("Set-Cookie",
            "refresh_token=abc123; max-age=604800; path=/api/v1/auth; secure; samesite=lax; httponly");

        var cookie = AuthTestClient.ReadSetCookie(response);

        Assert.NotNull(cookie);
        Assert.Equal("abc123", cookie.Value.ToString());
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal(TimeSpan.FromSeconds(604800), cookie.MaxAge);
        Assert.Null(AuthTestClient.ReadSetCookie(response, "khong_co"));
    }
}
