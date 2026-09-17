using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// TestServer để RemoteIpAddress = null → mọi request chung vùng rate limit "anon" của policy auth (10 req/phút/IP)
/// → test thứ 11 của lớp nhận 429, đỏ ngẫu nhiên theo thứ tự chạy (Đ-D7).
///
/// Đứng ĐẦU pipeline: có header <see cref="Header"/> thì dùng IP đó (test rate limit gửi cùng một IP), không thì sinh
/// IP ngẫu nhiên. Chỉ tồn tại trong assembly test — KHÔNG thêm cờ tắt rate limit vào src/backend/.
/// </summary>
public sealed class FakeRemoteIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nextMiddleware) =>
        {
            ctx.Connection.RemoteIpAddress = ctx.Request.Headers.TryGetValue(Header, out var ip)
                ? IPAddress.Parse(ip.ToString())
                : new IPAddress(RandomNumberGenerator.GetBytes(4));
            return nextMiddleware(ctx);
        });
        next(app);
    };
}
