using System.ComponentModel.DataAnnotations;
using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>Body của <c>POST /conversations</c> (Mục 8.1 <c>CreateConversation</c>). Chỉ MỘT id — người kia.</summary>
public sealed class CreateConversationRequest
{
    [Required]
    public Guid UserId { get; init; }
}

/// <summary>Body của <c>POST /conversations/{id}/messages</c> (Mục 8.1). Cùng hình dạng tham số hub <c>SendMessage</c> trừ id hội thoại.</summary>
public sealed class SendMessageRequest
{
    [Required]
    public string Content { get; init; } = "";

    [Required]
    public Guid ClientMsgId { get; init; }
}

/// <summary>Body của <c>POST /conversations/{id}/receipts</c> (Mục 8.1). <see cref="Kind"/> nullable để thiếu trường → 400, không mặc định.</summary>
public sealed class ReceiptRequest
{
    [Required]
    public ReceiptKind? Kind { get; init; }

    [Required]
    public long UpToSeq { get; init; }
}

/// <summary>Một tin (Mục 8.1 <c>MessageResponse</c>) — cũng là payload của sự kiện hub <c>MessageReceived</c>. Không có <c>status</c> (Đ-5.6).</summary>
public sealed record MessageResponse(
    Guid MessageId, Guid ConversationId, Guid SenderId, long Seq, string Content, Guid ClientMsgId, DateTimeOffset CreatedAt)
{
    public static MessageResponse From(Message m) =>
        new(m.Id, m.ConversationId, m.SenderId, m.Seq, m.Content, m.ClientMsgId, m.CreatedAt);
}

/// <summary>Người kia của hội thoại — <c>UserCard</c> định nghĩa lại trong hợp đồng này (Mục 8.1).</summary>
public sealed record ConversationPeer(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>
/// Một hội thoại nhìn từ phía người gọi (Mục 8.1). <see cref="CanSend"/> chỉ có ở <c>GET /{id}</c> và <c>POST</c> (Đ-5.3); danh
/// sách để <c>null</c> — tránh N+1 với <c>IFriendshipReader</c>.
/// </summary>
public sealed record ConversationResponse(
    Guid ConversationId,
    ConversationPeer Peer,
    MessageResponse? LastMessage,
    long UnreadCount,
    long PeerDeliveredSeq,
    long PeerSeenSeq,
    bool? CanSend);

public sealed record ConversationPage(IReadOnlyList<ConversationResponse> Items, string? NextCursor);

public sealed record MessagePage(IReadOnlyList<MessageResponse> Items, string? NextCursor);

public sealed record UnreadCountResponse(long Total);

/// <summary>Payload sự kiện hub <c>ReceiptUpdated</c> (chat-hub-v1.md Mục 3): mốc TUYỆT ĐỐI của <see cref="UserId"/>.</summary>
public sealed record ReceiptUpdatedEvent(Guid ConversationId, Guid UserId, long DeliveredSeq, long SeenSeq);

/// <summary>Tham số hub <c>SendMessage</c> (chat-hub-v1.md Mục 2).</summary>
public sealed record SendMessageArgs(Guid ConversationId, string? Content, Guid ClientMsgId);

/// <summary>Kết quả hub <c>SendMessage</c> — ACK "Đã gửi". <see cref="Replayed"/>: tin này đã lưu trước đó (Đ-5.5).</summary>
public sealed record SendMessageResult(MessageResponse Message, bool Replayed);

/// <summary>Tham số hub <c>SendReceipt</c> (chat-hub-v1.md Mục 2).</summary>
public sealed record SendReceiptArgs(Guid ConversationId, ReceiptKind Kind, long UpToSeq);
