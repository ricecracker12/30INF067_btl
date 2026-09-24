using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Messaging.Application;

/// <summary>
/// Event trong tiến trình sau <c>COMMIT</c> (Đ-5.15, bị Đ-6.2 thay: phát thẳng qua <see cref="IEventPublisher"/>, khuôn
/// <c>SocialGraphEvents</c> của GĐ4). GĐ6 nối thông báo <c>message</c> vào đây — chỉ khi người nhận offline (Đ-6.17).
///
/// Không log gì, không mang nội dung tin (Đ-5.18, <c>IntegrationEventShapeTests</c> canh): <c>Publish</c> không chờ handler,
/// không ném, bus tự đếm metric. Tên tham số nói VAI của từng id — đổi chỗ người gửi/người nhận là thông báo gửi nhầm người.
/// </summary>
public sealed class MessagingEvents(IEventPublisher publisher)
{
    /// <summary>Tin mới đã COMMIT. KHÔNG gọi khi gửi lại (<c>replayed</c>) — không có gì mới.</summary>
    public void MessageSent(Guid conversationId, Guid messageId, Guid senderId, Guid recipientId, long seq) =>
        publisher.Publish(new SharedKernel.Events.MessageSent(conversationId, messageId, senderId, recipientId, seq));
}
