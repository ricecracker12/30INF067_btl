using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using SocialApp.IntegrationTests.Harness;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Rate limit theo IP THẬT của người dùng khi đi qua proxy tin cậy (BFF Next.js), không theo IP của proxy — và không để
/// client tự khai IP qua X-Forwarded-For. IP kết nối giả bằng <see cref="FakeRemoteIpStartupFilter"/> (đứng trước
/// UseForwardedHeaders trong pipeline).
///
/// Mỗi test dựng app RIÊNG: hạn mức 10 req/phút/IP của policy "auth" tính theo từng app.
/// </summary>
public sealed class ForwardedClientIpTests
{
    private const string Login = "/api/v1/auth/login";
    private const string Bff = "127.0.0.1";   // loopback: ASP.NET tin sẵn — BFF cùng máy ở dev

    /// <summary>Body rỗng → 400 ở validator, không chạm DB (ApiFactory trỏ DB không tồn tại), vẫn đi qua rate limiter.</summary>
    private static async Task<HttpStatusCode> PostLoginAsync(HttpClient client, string remoteIp, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Login)
        {
            Content = JsonContent.Create(new { email = "", password = "" }),
        };
        request.Headers.Add(FakeRemoteIpStartupFilter.Header, remoteIp);
        if (forwardedFor is not null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Qua_proxy_tin_cay_hai_nguoi_dung_co_hai_han_muc_rieng()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, await PostLoginAsync(client, Bff, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostLoginAsync(client, Bff, "198.51.100.1"));

        // Cùng BFF, người dùng khác: KHÔNG ăn chung hạn mức với người thứ nhất.
        Assert.Equal(HttpStatusCode.BadRequest, await PostLoginAsync(client, Bff, "198.51.100.2"));
    }

    [Fact]
    public async Task Nguon_khong_tin_cay_tu_khai_X_Forwarded_For_khong_vuot_duoc_rate_limit()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        const string attacker = "203.0.113.66";

        // Mỗi request tự khai một IP khác — nếu header được tin, không bao giờ chạm 429.
        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, await PostLoginAsync(client, attacker, $"192.0.2.{i}"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostLoginAsync(client, attacker, "192.0.2.99"));
    }

    [Fact]
    public async Task Chi_lay_phan_tu_cuoi_do_proxy_gan_khong_lay_phan_client_tu_viet_phia_truoc()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        // Client gửi "X-Forwarded-For: <ip bịa>" tới BFF; BFF nối IP thật vào cuối. Chỉ phần cuối được dùng.
        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, await PostLoginAsync(client, Bff, $"10.9.9.{i}, 198.51.100.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostLoginAsync(client, Bff, "10.9.9.200, 198.51.100.7"));
    }

    [Fact]
    public async Task Mang_noi_bo_khai_trong_ReverseProxy_TrustedNetworks_duoc_tin()
    {
        await using var app = new ApiFactory().WithWebHostBuilder(b =>
            b.UseSetting("ReverseProxy:TrustedNetworks:0", "172.20.0.0/16"));
        using var client = app.CreateClient();
        const string bffContainer = "172.20.0.5";

        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, await PostLoginAsync(client, bffContainer, "198.51.100.30"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostLoginAsync(client, bffContainer, "198.51.100.30"));
        Assert.Equal(HttpStatusCode.BadRequest, await PostLoginAsync(client, bffContainer, "198.51.100.31"));
    }

    [Theory]
    [InlineData("ReverseProxy:TrustedNetworks:0", "172.20.0.0")]      // thiếu /prefix
    [InlineData("ReverseProxy:TrustedNetworks:0", "172.20.0.0/99")]
    [InlineData("ReverseProxy:TrustedProxies:0", "khong-phai-ip")]
    public async Task Cau_hinh_proxy_sai_dang_thi_tu_choi_khoi_dong(string key, string value)
    {
        await using var app = new ApiFactory().WithWebHostBuilder(b => b.UseSetting(key, value));

        var ex = Assert.Throws<InvalidOperationException>(() => app.CreateClient());

        Assert.Contains(value, ex.Message, StringComparison.Ordinal);
        Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
    }
}
