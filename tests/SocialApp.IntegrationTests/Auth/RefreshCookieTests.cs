using System.Net;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D4 — cookie refresh do login thật phát ra mang đủ 5 thuộc tính của hợp đồng (<c>SetRefreshCookie</c>). Kỳ vọng viết tay:
/// Max-Age 604800 = 7 ngày. Chuỗi Set-Cookie ASP.NET ghi thường (<c>httponly</c>, <c>samesite=lax</c>) nên so cả dạng đã parse
/// lẫn chuỗi thô không phân biệt hoa thường. Xóa cookie (Max-Age=0) kiểm ở unit test RefreshCookieFormatTests và ở D5/D6.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RefreshCookieTests(PostgresFixture postgres, IdentityApiFactory factory)
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
    public async Task Login_thanh_cong_Set_Cookie_du_5_thuoc_tinh_cua_hop_dong()
    {
        var email = AuthTestClient.NewEmail();
        await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);

        using var response = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = AuthTestClient.ReadSetCookie(response);
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly, "thiếu HttpOnly");
        Assert.True(cookie.Secure, "thiếu Secure");
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
        Assert.Equal(TimeSpan.FromSeconds(604800), cookie.MaxAge);
        Assert.True(cookie.Domain.Length == 0, "không đặt Domain — cookie chỉ gắn với host của API");

        var raw = string.Join(";", response.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        foreach (var attribute in new[] { "httponly", "secure", "samesite=lax", "path=/api/v1/auth", "max-age=604800" })
            Assert.Contains(attribute, raw);
    }
}
