using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Notification.Application;

namespace SocialApp.Modules.Notification.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="INotificationQueries"/> — LINQ <c>AsNoTracking</c> và <c>ExecuteUpdate</c> trên <see cref="NotificationDbContext"/>.
///
/// Danh sách: keyset <c>(updated_at DESC, id DESC)</c> với <c>recipient_id</c> đứng đầu — đúng <c>idx_notifications_recent</c>. Đếm chưa đọc:
/// <c>recipient_id = @me AND is_read = false</c> — đúng điều kiện của index một phần <c>idx_notifications_unread</c>.
///
/// Đánh dấu đã đọc bằng <c>ExecuteUpdateAsync</c> chỉ đặt <c>is_read</c> — KHÔNG tracking + <c>SaveChanges</c>: một ai đó sau này thêm đóng
/// dấu <c>updated_at</c> vào <c>SaveChanges</c> "cho đồng bộ" là thông báo cũ nhảy lên đầu (cạm bẫy 1, <c>NOTIF-10</c> bắt).
/// </summary>
internal sealed class NotificationQueries(NotificationDbContext db) : INotificationQueries
{
    public async Task<IReadOnlyList<NotificationRow>> ListAsync(
        Guid recipientId, NotificationCursor? after, int take, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == recipientId);
        if (after is { } at)
            query = query.Where(n => n.UpdatedAt < at.UpdatedAt
                                  || (n.UpdatedAt == at.UpdatedAt && n.Id.CompareTo(at.Id) < 0));

        return await query
            .OrderByDescending(n => n.UpdatedAt)
            .ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new NotificationRow(
                n.Id, n.Type, n.TargetType, n.TargetId, n.PostId, n.LastActorId, n.ActorCount, n.ReasonCode, n.IsRead, n.CreatedAt,
                n.UpdatedAt))
            .ToListAsync(ct);
    }

    public Task<int> CountUnreadAsync(Guid recipientId, CancellationToken ct) =>
        db.Notifications.CountAsync(n => n.RecipientId == recipientId && !n.IsRead, ct);

    /// <summary>Theo <c>uq_notifications_group (recipient_id, group_key)</c> — đúng một dòng hoặc không.</summary>
    public Task<NotificationRow?> FindGroupAsync(Guid recipientId, string groupKey, CancellationToken ct) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == recipientId && n.GroupKey == groupKey)
            .Select(n => new NotificationRow(
                n.Id, n.Type, n.TargetType, n.TargetId, n.PostId, n.LastActorId, n.ActorCount, n.ReasonCode, n.IsRead, n.CreatedAt,
                n.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    /// <summary>
    /// Điều kiện KHÔNG có <c>is_read = false</c>: đã đọc rồi vẫn khớp một dòng → 204, không 403. Có vế đó thì bấm lại một thông báo đã đọc
    /// (hai tab) nhận 403 như thể thông báo của người khác.
    /// </summary>
    public async Task<bool> MarkReadAsync(Guid recipientId, Guid notificationId, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.Id == notificationId && n.RecipientId == recipientId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct) == 1;

    public Task MarkAllReadAsync(Guid recipientId, DateTimeOffset upTo, CancellationToken ct) =>
        db.Notifications
            .Where(n => n.RecipientId == recipientId && !n.IsRead && n.UpdatedAt <= upTo)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
}
