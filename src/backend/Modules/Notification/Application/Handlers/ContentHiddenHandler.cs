using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Notification.Application.Handlers;

/// <summary>
/// <c>moderation</c> (Đ-6.17, B.10 #7): tác giả nội dung bị ẩn nhận thông báo kèm mã lý do. KHÔNG actor — event không mang id Moderator
/// hay người báo, và store từ chối <c>moderation</c> có người, nên không có đường nào để id đó lọt vào bảng thông báo.
///
/// Không tự báo mình không áp ở đây: Moderator ẩn bài của chính mình vẫn nhận thông báo (không có actor để so).
/// Không bắt ngoại lệ — xem <see cref="FriendRequestSentHandler"/>.
/// </summary>
public sealed class ContentHiddenHandler(INotificationStore store) : IIntegrationEventHandler<ContentHidden>
{
    public Task HandleAsync(ContentHidden integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return store.UpsertAsync(For(integrationEvent), ct);
    }

    public static NotificationUpsert For(ContentHidden e) => new(
        RecipientId: e.AuthorId,
        Type: NotificationTypes.Moderation,
        GroupKey: GroupKey.Moderation(e.TargetType, e.TargetId),
        TargetType: NotificationTargetTypes.From(e.TargetType),
        TargetId: e.TargetId,
        PostId: e.PostId,
        ActorId: null,
        ReasonCode: e.ReasonCode);
}
