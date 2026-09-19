namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Body 200 của MỌI endpoint trả hồ sơ (<c>GET /users/{userId}/profile</c> ở D1, <c>PUT /users/me/profile</c> ở D2,
/// <c>PUT/DELETE /users/me/avatar</c> ở D3) — khớp schema <c>ProfileResponse</c> của <c>profile-v1.yaml</c>. Một hình dạng,
/// không có bản rút gọn: hợp đồng đã chốt như vậy và FE chỉ sinh một kiểu.
///
/// Là <c>record</c> riêng chứ KHÔNG trả entity <see cref="Domain.UserProfile"/>: entity mang <c>AvatarKey</c>, tức là
/// <c>storage_key</c> thô. Trả nó ra là lộ key của người dùng — đúng thứ Đ-2.9 dựng <see cref="AvatarUrl"/> để tránh — và
/// Swagger sẽ sinh schema có trường đó, làm cổng hợp đồng (B4) đỏ.
///
/// <paramref name="AvatarUrl"/> là presigned GET 15 phút (Đ-2.9), <c>null</c> khi chưa đặt avatar. Ký ở SERVICE, không ở
/// store (store không biết R2) và không ở controller (controller không có logic nào).
///
/// <paramref name="Bio"/> <c>null</c> = chưa đặt. Hợp đồng để <c>bio</c> và <c>avatarUrl</c> ngoài <c>required</c> nhưng
/// record vẫn luôn phát ra cả hai với giá trị <c>null</c> — FE đọc <c>bio ?? ''</c>, không phải phân biệt "vắng" với "null".
/// </summary>
public sealed record ProfileResponse(
    Guid UserId,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
