namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// FR-003 (chốt 2026-09-14): sai mật khẩu <see cref="MaxFailedAttempts"/> lần LIÊN TIẾP thì khóa <see cref="LockDuration"/>.
/// Không có cửa sổ thời gian cho các lần sai; đăng nhập đúng thì bộ đếm về 0. Luật nghiệp vụ nên nằm ở Domain; store
/// dùng hai hằng số này trong câu UPDATE nguyên tử (giai-doan-1.md Mục 7.2).
/// </summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
}
