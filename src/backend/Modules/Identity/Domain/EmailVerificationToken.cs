using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Token xác minh email, dùng MỘT LẦN (bảng <c>email_verification_tokens</c>). Giống refresh token
/// ở chỗ chỉ lưu <see cref="TokenHash"/> SHA-256 hex — bản rõ chỉ nằm trong đường link gửi qua mail.
///
/// <see cref="ConsumedAt"/> đánh dấu đã dùng, thay vì xóa dòng: giữ lại để phân biệt "link sai" với
/// "link đã dùng rồi" khi trả lỗi.
/// </summary>
public sealed class EmailVerificationToken
{
    /// <summary>Khóa chính UUID v7.</summary>
    public Guid Id { get; init; } = Uuid7.New();

    /// <summary>FK → <c>users.user_id</c>, ON DELETE CASCADE.</summary>
    public required Guid UserId { get; init; }

    /// <summary>SHA-256 hex của bản rõ, <c>varchar(64)</c>, unique.</summary>
    public required string TokenHash { get; init; }

    /// <summary>Hạn dùng.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Thời điểm token được dùng; <c>null</c> nghĩa là chưa dùng.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}
