using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Realtime;

namespace SocialApp.Modules.Notification.Application.Handlers;

/// <summary>
/// <c>message</c> (Đ-6.17, UC-15 A1): người nhận tin có thông báo CHỈ KHI đang offline — không có kết nối hub nào (<see cref="IPresenceReader"/>
/// của GĐ5, Đ-5.11). Người đang mở app đã thấy tin qua hub và badge chưa đọc của màn chat (Đ-5.14); tạo thông báo cho họ là mỗi tin một
/// tiếng chuông trong lúc đang chat. Presence không đọc được (Redis chết) → coi là offline: thừa một thông báo hơn là mất một thông báo.
///
/// Nhóm <c>message:{conversationId}</c> — tin tới dồn vào một dòng mỗi hội thoại, đích là hội thoại. Event không mang nội dung tin
/// (Đ-5.18), thông báo cũng không. Presence đọc ngay lúc xử lý event (sau <c>COMMIT</c> của tin): người nhận vừa ngắt vài giây trước vẫn
/// có thể còn "online" tới khi kết nối hết hạn (≤ 90 giây, Đ-5.11) — chấp nhận.
///
/// Không bắt ngoại lệ — xem <see cref="FriendRequestSentHandler"/>.
/// </summary>
public sealed class MessageSentHandler(INotificationStore store, IPresenceReader presence) : IIntegrationEventHandler<MessageSent>
{
    public async Task HandleAsync(MessageSent integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (await presence.IsOnlineAsync(integrationEvent.RecipientId, ct))
            return;

        await store.UpsertAsync(For(integrationEvent), ct);
    }

    public static NotificationUpsert For(MessageSent e) => new(
        RecipientId: e.RecipientId,
        Type: NotificationTypes.Message,
        GroupKey: GroupKey.Message(e.ConversationId),
        TargetType: NotificationTargetTypes.Conversation,
        TargetId: e.ConversationId,
        PostId: null,
        ActorId: e.SenderId,
        ReasonCode: null);
}
