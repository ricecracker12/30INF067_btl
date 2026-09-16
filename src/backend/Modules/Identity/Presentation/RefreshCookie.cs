using Microsoft.AspNetCore.Http;

namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Cookie refresh token — chỗ DUY NHẤT tạo, đọc, xóa nó. Cả 5 thuộc tính là HỢP ĐỒNG (identity-v1.yaml, SetRefreshCookie),
/// không phải chi tiết cài đặt. Set và Clear dùng CÙNG Options: Path lệch một ký tự là trình duyệt giữ cookie cũ sau logout.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "refresh_token";
    public const string Path = "/api/v1/auth";

    private static CookieOptions Options(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,                  // trình duyệt vẫn nhận trên http://localhost
        SameSite = SameSiteMode.Lax,
        Path = Path,
        MaxAge = maxAge,
        IsEssential = true,
    };

    /// <summary><paramref name="days"/> đọc từ CÙNG JwtOptions.RefreshTokenDays với expires_at trong DB.</summary>
    public static void Set(HttpResponse response, string plainToken, int days) =>
        response.Cookies.Append(Name, plainToken, Options(TimeSpan.FromDays(days)));

    /// <summary>Max-Age=0 thay vì Cookies.Delete (ghi expires=1970) — khớp nguyên văn ClearRefreshCookie.</summary>
    public static void Clear(HttpResponse response) =>
        response.Cookies.Append(Name, "", Options(TimeSpan.Zero));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
