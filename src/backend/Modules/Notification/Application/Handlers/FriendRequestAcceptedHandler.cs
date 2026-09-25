using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Notification.Application.Handlers;

/// <summary>
/// <c>friend_accepted</c> (Đ-6.17): người đã GỬI lời mời nhận thông báo, người bấm chấp nhận là actor và là đích. Đổi chỗ hai id vẫn
/// compile và thông báo tới chính người vừa bấm — <see cref="For"/> và <c>EVT-06</c> là hai lưới.
///
/// Không bắt ngoại lệ — xem <see cref="FriendRequestSentHandler"/>.
/// </summary>
public sealed class FriendRequestAcceptedHandler(INotificationStore store) : IIntegrationEventHandler<FriendRequestAccepted>
{
    public Task HandleAsync(FriendRequestAccepted integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return store.UpsertAsync(For(integrationEvent), ct);
    }

    public static NotificationUpsert For(FriendRequestAccepted e) => new(
        RecipientId: e.RequesterId,
        Type: NotificationTypes.FriendAccepted,
        GroupKey: GroupKey.FriendAccepted(e.AccepterId),
        TargetType: NotificationTargetTypes.User,
        TargetId: e.AccepterId,
        PostId: null,
        ActorId: e.AccepterId,
        ReasonCode: null);
}
