using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Notification.Application.Handlers;

/// <summary>
/// <c>reaction</c> (Đ-6.17): tác giả bài/bình luận nhận thông báo khi có người bày tỏ cảm xúc MỚI. Đổi loại (<c>IsNew = false</c>) không
/// phải "có gì mới" → không làm gì, kể cả không đẩy nhóm lên đầu. Thả, gỡ, thả lại là hai lần <c>IsNew = true</c> của cùng một người — store
/// đếm người, không đếm lượt (<c>NOTIF-04</c>).
///
/// Không rút lại thông báo khi gỡ cảm xúc (Đ-6.16). Không bắt ngoại lệ — xem <see cref="FriendRequestSentHandler"/>.
/// </summary>
public sealed class ReactionSetHandler(INotificationStore store) : IIntegrationEventHandler<ReactionSet>
{
    public Task HandleAsync(ReactionSet integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return For(integrationEvent) is { } upsert ? store.UpsertAsync(upsert, ct) : Task.CompletedTask;
    }

    /// <returns><c>null</c> khi chỉ đổi loại cảm xúc.</returns>
    public static NotificationUpsert? For(ReactionSet e) => e.IsNew
        ? new NotificationUpsert(
            RecipientId: e.TargetAuthorId,
            Type: NotificationTypes.Reaction,
            GroupKey: GroupKey.Reaction(e.TargetType, e.TargetId),
            TargetType: NotificationTargetTypes.From(e.TargetType),
            TargetId: e.TargetId,
            PostId: e.PostId,
            ActorId: e.ActorId,
            ReasonCode: null)
        : null;
}
