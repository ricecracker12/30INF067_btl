namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Cảm xúc của một người với một đối tượng (ENT-05, bảng <c>content.reactions</c>) — <b>KHUNG</b> của
/// Đ-2.12, giống <see cref="Comment"/>: đủ ràng buộc, không một endpoint nào ở GĐ2.
///
/// KHÔNG có cột <c>reaction_id</c>, và đó là điểm chính: khóa chính là BA cột
/// <c>(user_id, target_type, target_id)</c> — <b>chính nó là BR-05</b> (một người, một đối tượng, một cảm
/// xúc). Thêm một khóa đơn cho "cho giống các bảng khác" là xóa mất BR-05 khỏi tầng DB, và không test nào
/// của GĐ2 bắt được vì GĐ2 chưa ghi bảng này.
/// </summary>
public sealed class Reaction
{
    /// <summary>Người thả cảm xúc, bằng <c>identity.users.user_id</c> — KHÔNG FK chéo schema. Phần 1/3 của khóa chính.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Bài hay bình luận. DB có CHECK <c>ck_reactions_target</c> canh lại. Phần 2/3 của khóa chính.</summary>
    public required ReactionTargetType TargetType { get; init; }

    /// <summary><c>post_id</c> hoặc <c>comment_id</c> — bảng đa hình nên KHÔNG FK. Phần 3/3 của khóa chính.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Loại cảm xúc. DB có CHECK <c>ck_reactions_type</c> canh lại.</summary>
    public required ReactionType Type { get; set; }

    /// <summary>Thời điểm tạo, do đồng hồ app gán.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm đổi cảm xúc gần nhất — do override <c>SaveChanges</c> của <c>ContentDbContext</c> (A5) đóng dấu.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
