using Microsoft.Extensions.Logging;

namespace SocialApp.Modules.Notification.Application;

/// <summary>
/// Sự kiện hub <c>NotificationUpserted</c> (Mục 8.4, <c>notification-hub-v1.md</c>): nhóm vừa có sự kiện mới + số nhóm chưa đọc TUYỆT ĐỐI.
/// <c>notificationId</c> là khóa khử trùng phía FE; <see cref="UnreadTotal"/> tuyệt đối nên nhận hai lần hay sai thứ tự đều vô hại.
/// </summary>
public sealed record NotificationUpsertedEvent(NotificationResponse Notification, int UnreadTotal);

/// <summary>
/// Đẩy realtime tới MỌI kết nối hub của một người (C6, Đ-6.18). Application định nghĩa, Presentation hiện thực bằng <c>IHubContext</c> —
/// khuôn <c>IChatNotifier</c> của GĐ5: service không biết SignalR. Người nhận do server chọn (người nhận của dòng thông báo), không group,
/// không <c>Clients.All</c>.
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid recipientId, NotificationUpsertedEvent notification, CancellationToken ct);
}

/// <summary>
/// Bọc <see cref="INotificationStore"/>: sau khi upsert đã <c>COMMIT</c> (store trả về), đọc lại nhóm + số chưa đọc rồi đẩy qua hub (C6).
/// Bộ trang trí thay vì sửa từng handler: sáu handler (D10, bước 9) chỉ biết <see cref="INotificationStore"/>, và đường đẩy đi cùng MỌI
/// đường ghi — không handler nào quên được.
///
/// <b>Đẩy hỏng không làm hỏng thông báo:</b> dòng đã lưu, FE thấy ở lượt hỏi lại 30 giây (Đ-6.18 — đường lùi vĩnh viễn). Mọi lỗi của phần
/// đẩy (đọc lại, hub) → log Warning, KHÔNG ném — ném ra thì bus ghi "handler hỏng" cho một thông báo đã lưu đúng. Lỗi của chính upsert thì
/// vẫn ném như cũ. Log chỉ mang loại lỗi — không id người, không khóa gộp (B.10 #5).
/// </summary>
public sealed class PushingNotificationStore(
    INotificationStore inner,
    NotificationService reads,
    INotificationPusher pusher,
    ILogger<PushingNotificationStore> logger) : INotificationStore
{
    public async Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upsert);
        var result = await inner.UpsertAsync(upsert, ct);
        if (result == UpsertResult.Skipped)
            return result;

        try
        {
            if (await reads.UpsertedEventAsync(upsert.RecipientId, upsert.GroupKey, ct) is { } pushed)
                await pusher.PushAsync(upsert.RecipientId, pushed, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Không đẩy được thông báo qua hub — thông báo đã lưu, client thấy ở lượt hỏi lại");
        }

        return result;
    }
}
