using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Notification.Application.Handlers;

/// <summary>
/// <c>friend_request</c> (Đ-6.17): người được mời nhận thông báo, người mời là actor và là đích (bấm vào → trang cá nhân người mời).
/// Mời → hủy → mời lại làm sáng lại ĐÚNG nhóm cũ (<see cref="GroupKey.FriendRequest"/> theo người mời), không thêm dòng.
///
/// Không bắt ngoại lệ: bus (C0) bắt, log tên handler + tên event + số thứ tự phong bì, rồi chạy tiếp (<c>EVT-02</c>). Bắt rồi nuốt ở đây
/// là lỗi biến mất khỏi log.
/// </summary>
public sealed class FriendRequestSentHandler(INotificationStore store) : IIntegrationEventHandler<FriendRequestSent>
{
    public Task HandleAsync(FriendRequestSent integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return store.UpsertAsync(For(integrationEvent), ct);
    }

    /// <summary>Phép dịch event → thông báo, tách riêng để unit test khẳng định đúng VAI từng id (hai <see cref="Guid"/> đổi chỗ vẫn compile).</summary>
    public static NotificationUpsert For(FriendRequestSent e) => new(
        RecipientId: e.AddresseeId,
        Type: NotificationTypes.FriendRequest,
        GroupKey: GroupKey.FriendRequest(e.RequesterId),
        TargetType: NotificationTargetTypes.User,
        TargetId: e.RequesterId,
        PostId: null,
        ActorId: e.RequesterId,
        ReasonCode: null);
}
