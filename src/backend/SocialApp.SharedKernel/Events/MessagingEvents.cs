namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Phát bởi Messaging (B — GĐ5) sau <c>COMMIT</c> của gửi tin (Đ-5.15). KHÔNG mang nội dung tin (Đ-5.18). GĐ6 tiêu thụ: thông
/// báo <c>message</c> cho <paramref name="RecipientId"/> — chỉ khi người đó offline (Đ-6.17).
/// </summary>
/// <param name="Seq">Số thứ tự của tin trong hội thoại.</param>
public sealed record MessageSent(
    Guid ConversationId,
    Guid MessageId,
    Guid SenderId,
    Guid RecipientId,
    long Seq) : IIntegrationEvent;
