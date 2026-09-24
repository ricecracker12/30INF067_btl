namespace SocialApp.Modules.Identity.Application;

/// <summary>
/// Đủ dữ liệu cho 6 bước đăng nhập (giai-doan-1.md Mục 7.2). <paramref name="RoleCode"/> là roles.code CHUỖI đã join —
/// JWT không bao giờ mang role_id (Mục 3.1). <paramref name="Status"/> (GĐ6, Đ-6.5) là <c>users.status</c> — bước 4b từ chối
/// <c>disabled</c>.
/// </summary>
public sealed record LoginCandidate(
    Guid UserId,
    string PasswordHash,
    string RoleCode,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? LockedUntil,
    string Status);
