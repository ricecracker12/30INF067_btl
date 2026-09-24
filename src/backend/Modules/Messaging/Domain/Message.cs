namespace SocialApp.Modules.Messaging.Domain;

/// <summary>
/// ENT-07 <c>messaging.messages</c>. Tin không sửa được trong MVP nên KHÔNG có <c>updated_at</c> (Mục 4, chỗ dễ sai số 1) và
/// KHÔNG có cột <c>status</c> (Đ-5.6 — trạng thái suy từ mốc trên <see cref="Conversation"/>).
///
/// <see cref="Id"/> do app sinh (UUID v7) TRƯỚC transaction gửi tin, để câu UPDATE con trỏ <c>last_message_id</c> ở bước 4
/// của Đ-5.4 chạy trước INSERT ở bước 5. <see cref="SenderId"/> là uuid trần, không FK (Đ-5.1).
/// </summary>
public sealed class Message
{
    public Guid Id { get; init; }

    public Guid ConversationId { get; init; }

    public Guid SenderId { get; init; }

    /// <summary>Số thứ tự trong hội thoại: 1, 2, 3… không lỗ, không trùng (<c>uq_messages_conv_seq</c>).</summary>
    public long Seq { get; init; }

    public string Content { get; init; } = "";

    /// <summary>Khóa idempotency do client sinh (<c>uq_messages_conv_client_id</c>, US-015 AC-03, Đ-5.5).</summary>
    public Guid ClientMsgId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
