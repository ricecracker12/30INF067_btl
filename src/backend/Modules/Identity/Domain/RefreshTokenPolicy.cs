namespace SocialApp.Modules.Identity.Domain;

/// <summary>Luật xoay vòng refresh token (NFR-SEC-03, giai-doan-1.md Mục 7.3).</summary>
public static class RefreshTokenPolicy
{
    /// <summary>
    /// Ân hạn cho race hai tab (Đ-D3): token đã bị XOAY trong khoảng này và family còn ít nhất một token sống → phát một
    /// token mới cùng family thay vì coi là reuse. Đánh đổi đã chấp nhận: trong 10 giây, kẻ cầm token cũ cũng đổi được.
    /// </summary>
    public static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);
}
