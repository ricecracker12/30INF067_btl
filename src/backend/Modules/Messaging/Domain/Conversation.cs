namespace SocialApp.Modules.Messaging.Domain;

/// <summary>
/// ENT-06 <c>messaging.conversations</c> — đúng một hội thoại cho mỗi cặp người dùng (Đ-5.2), hai thành viên cố định.
///
/// Không có navigation sang người dùng: <see cref="UserAId"/>, <see cref="UserBId"/> là uuid trần, không FK chéo schema
/// (Đ-5.1). Không có navigation sang <see cref="Message"/>: con trỏ <see cref="LastMessageId"/> không FK (hai bảng trỏ vòng
/// vào nhau) và được ghi trong CÙNG transaction với tin (Đ-5.4).
///
/// Bốn cột mốc thay cột <c>messages.status</c> của PTTK (Đ-5.6, chủ dự án đồng ý 2026-09-24): "đã nhận/đã xem mọi tin có
/// <c>seq</c> ≤ N". Chỉ tăng (<c>GREATEST</c>), đã xem kéo theo đã nhận, không vượt <see cref="SeqCounter"/>.
/// </summary>
public sealed class Conversation
{
    public Guid Id { get; init; }

    public Guid UserAId { get; init; }

    public Guid UserBId { get; init; }

    /// <summary><c>seq</c> đã cấp gần nhất; tin kế tiếp nhận <c>SeqCounter + 1</c> (Đ-5.4).</summary>
    public long SeqCounter { get; set; }

    public Guid? LastMessageId { get; set; }

    /// <summary>Khóa sắp danh sách hội thoại. <c>null</c> = chưa có tin nào — hội thoại rỗng không hiện trong danh sách.</summary>
    public DateTimeOffset? LastMessageAt { get; set; }

    public long UserADeliveredSeq { get; set; }

    public long UserASeenSeq { get; set; }

    public long UserBDeliveredSeq { get; set; }

    public long UserBSeenSeq { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static Conversation Start(Guid id, ConversationPair pair, DateTimeOffset now) => new()
    {
        Id = id,
        UserAId = pair.UserA,
        UserBId = pair.UserB,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>BR-06: chỉ hai thành viên. Chỗ DUY NHẤT dùng hàm này để phân quyền là <c>ConversationAccess</c>.</summary>
    public bool IsMember(Guid userId) => userId == UserAId || userId == UserBId;

    /// <summary>Người còn lại. Người gọi phải là thành viên — không phải thì ném (lỗi lập trình, không phải lỗi người dùng).</summary>
    public Guid PeerOf(Guid userId)
    {
        if (userId == UserAId)
            return UserBId;
        if (userId == UserBId)
            return UserAId;

        throw new ArgumentException("userId không phải thành viên của hội thoại này.", nameof(userId));
    }

    /// <summary>Mốc đã nhận / đã xem của <paramref name="userId"/> (thành viên).</summary>
    public ReceiptMarks MarksOf(Guid userId)
    {
        if (userId == UserAId)
            return new ReceiptMarks(UserADeliveredSeq, UserASeenSeq);
        if (userId == UserBId)
            return new ReceiptMarks(UserBDeliveredSeq, UserBSeenSeq);

        throw new ArgumentException("userId không phải thành viên của hội thoại này.", nameof(userId));
    }
}
