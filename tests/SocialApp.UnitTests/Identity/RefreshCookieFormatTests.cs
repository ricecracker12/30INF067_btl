using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SocialApp.Modules.Identity.Presentation;
using Xunit;
using SameSiteMode = Microsoft.Net.Http.Headers.SameSiteMode;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// D4 — <see cref="RefreshCookie"/> trên <see cref="DefaultHttpContext"/>, không dựng app. Canh đúng bẫy 3 của D4: xóa cookie mà
/// khác Path lúc set thì trình duyệt giữ nguyên cookie cũ sau logout. Giá trị kỳ vọng viết tay theo hợp đồng.
/// </summary>
public sealed class RefreshCookieFormatTests
{
    private static SetCookieHeaderValue WrittenCookie(Action<HttpResponse> write)
    {
        var context = new DefaultHttpContext();
        write(context.Response);
        return SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void Set_ghi_du_5_thuoc_tinh_va_Max_Age_theo_so_ngay()
    {
        var cookie = WrittenCookie(r => RefreshCookie.Set(r, "abc123", days: 7));

        Assert.Equal("refresh_token", cookie.Name.ToString());
        Assert.Equal("abc123", cookie.Value.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
        Assert.Equal(TimeSpan.FromSeconds(604800), cookie.MaxAge);
    }

    [Fact]
    public void Clear_Max_Age_0_va_CUNG_Path_cung_thuoc_tinh_voi_Set()
    {
        var cookie = WrittenCookie(RefreshCookie.Clear);

        Assert.Equal("refresh_token", cookie.Name.ToString());
        Assert.Equal("", cookie.Value.ToString());
        Assert.Equal(TimeSpan.Zero, cookie.MaxAge);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
    }

    [Theory]
    [InlineData("refresh_token=abc123", "abc123")]
    [InlineData("khac=1; refresh_token=abc123", "abc123")]
    [InlineData("refresh_token=", null)]
    [InlineData("khac=1", null)]
    public void Read_chi_tra_gia_tri_khong_rong(string cookieHeader, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookieHeader;

        Assert.Equal(expected, RefreshCookie.Read(context.Request));
    }
}
