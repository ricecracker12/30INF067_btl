using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SocialApp.SharedKernel.DependencyInjection;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// C4: rate limiter phân vùng theo claim <c>sub</c>, đọc THẲNG claim chứ không qua <c>Identity.Name</c>. Nếu
/// đọc Name thì quên NameClaimType ở JwtBearer là mọi user chung vùng "user:" — chung 100 req/phút, hỏng câm.
/// Principal trong test cố ý KHÔNG đặt NameClaimType nên Identity.Name là null, đúng tình huống đó.
/// </summary>
public sealed class RateLimitPartitionKeyTests
{
    private static DefaultHttpContext Authenticated(params Claim[] claims) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")),
    };

    [Fact]
    public void Co_sub_thi_phan_vung_theo_user_du_Identity_Name_null()
    {
        var ctx = Authenticated(new Claim("sub", "u1"));

        Assert.Null(ctx.User.Identity!.Name);
        Assert.Equal("user:u1", SharedKernelExtensions.RateLimitPartitionKey(ctx));
    }

    [Fact]
    public void Hai_user_thi_hai_vung_khac_nhau()
    {
        Assert.NotEqual(
            SharedKernelExtensions.RateLimitPartitionKey(Authenticated(new Claim("sub", "u1"))),
            SharedKernelExtensions.RateLimitPartitionKey(Authenticated(new Claim("sub", "u2"))));
    }

    [Fact]
    public void An_danh_thi_phan_vung_theo_ip()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal("ip:203.0.113.7", SharedKernelExtensions.RateLimitPartitionKey(ctx));
    }

    [Fact]
    public void Da_xac_thuc_ma_khong_co_sub_thi_roi_ve_ip_khong_gop_chung_vung_user()
    {
        var ctx = Authenticated(new Claim("role", "USER"));
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");

        Assert.Equal("ip:203.0.113.8", SharedKernelExtensions.RateLimitPartitionKey(ctx));
    }
}
