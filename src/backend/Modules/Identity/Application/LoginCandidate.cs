namespace SocialApp.Modules.Identity.Application;

/// <summary>
/// Đủ dữ liệu cho 6 bước đăng nhập (giai-doan-1.md Mục 7.2). <paramref name="RoleCode"/> là roles.code CHUỖI đã join —
/// JWT không bao giờ mang role_id (Mục 3.1).
/// </summary>
public sealed record LoginCandidate(
    Guid UserId,
    string PasswordHash,
    string RoleCode,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? LockedUntil);
