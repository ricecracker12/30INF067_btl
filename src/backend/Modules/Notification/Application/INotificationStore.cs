using SocialApp.Modules.Notification.Domain;

namespace SocialApp.Modules.Notification.Application;

/// <summary>
/// Một sự kiện đã quy về "một dòng thông báo của một người nhận" — thứ handler D10 dựng từ event, store D9 gộp vào nhóm.
///
/// Không mang tên người, không mang trích đoạn nội dung (Đ-6.16): danh sách hydrate <see cref="ActorId"/> lúc đọc.
/// </summary>
/// <param name="Type">Một trong <see cref="NotificationTypes"/>.</param>
/// <param name="GroupKey">Dựng bằng <see cref="Domain.GroupKey"/> — không gõ tay.</param>
/// <param name="TargetType">Một trong <see cref="NotificationTargetTypes"/>.</param>
/// <param name="PostId">Bài chứa đích khi đích là bình luận — để FE dẫn tới bài.</param>
/// <param name="ActorId"><c>null</c> ĐÚNG KHI <see cref="Type"/> là <see cref="NotificationTypes.Moderation"/> — không lộ ai kiểm duyệt.
/// Mọi loại khác đều có người gây ra sự kiện.</param>
/// <param name="ReasonCode">Chỉ <see cref="NotificationTypes.Moderation"/>: mã lý do trong tập cố định của Moderation.</param>
public sealed record NotificationUpsert(
    Guid RecipientId,
    string Type,
    string GroupKey,
    string TargetType,
    Guid TargetId,
    Guid? PostId,
    Guid? ActorId,
    string? ReasonCode);

/// <summary>Kết quả một lần upsert — test và (sau này) pusher C6 đọc.</summary>
public enum UpsertResult
{
    /// <summary>Tự báo mình (<c>ActorId == RecipientId</c>) — không đụng DB.</summary>
    Skipped,

    /// <summary>Nhóm chưa có — vừa chèn dòng mới.</summary>
    Created,

    /// <summary>Nhóm đã có — sự kiện dồn vào dòng cũ (cùng đợt, hoặc mở đợt mới khi nhóm đã đọc).</summary>
    Updated,
}

/// <summary>
/// Đường GHI của thông báo (GĐ6 D9, Đ-6.16): chỗ DUY NHẤT biến "một sự kiện" thành "một dòng thông báo đã gộp". Đọc và đánh dấu đã
/// đọc (D11) ở interface riêng — hai đường không chung phụ thuộc (khuôn <c>IReportStore</c> / <c>IReportQueries</c> của Moderation).
/// </summary>
public interface INotificationStore
{
    /// <summary>
    /// Gộp sự kiện vào nhóm <c>(RecipientId, GroupKey)</c>, đếm người KHÁC NHAU trong ĐỢT hiện tại. Đúng dưới đồng thời: 20 người
    /// cùng lúc vào một nhóm chưa có ra MỘT dòng <c>actor_count = 20</c> (<c>NOTIF-C1</c>).
    ///
    /// Tự báo mình → <see cref="UpsertResult.Skipped"/> ngay ở đây, không handler nào phải nhớ canh. Sai cặp loại/người
    /// (<c>moderation</c> có người, loại khác không người) → <see cref="ArgumentException"/> trước mọi I/O: một handler viết nhầm
    /// không được ghi id Moderator vào DB.
    /// </summary>
    Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct);
}
