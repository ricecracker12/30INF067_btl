namespace SocialApp.Modules.Notification.Domain;

/// <summary>
/// Một NHÓM thông báo đã gộp của một người nhận (ENT-09, bảng <c>notification.notifications</c>) — Đ-6.16. Mỗi
/// <c>(RecipientId, GroupKey)</c> đúng một dòng (<c>uq_notifications_group</c>); sự kiện mới cùng nhóm cập nhật dòng đó, không
/// chèn dòng mới.
///
/// Tên lớp là <c>UserNotification</c>, không phải <c>Notification</c>: trong namespace <c>SocialApp.Modules.Notification.*</c>
/// tên <c>Notification</c> được tra ra NAMESPACE <c>SocialApp.Modules.Notification</c> trước khi tới type (CS0118) — cùng lý do
/// Profile đặt <c>UserProfile</c>.
///
/// Không lưu tên người, không lưu trích đoạn nội dung (Đ-6.16): danh sách hydrate <see cref="LastActorId"/> qua
/// <c>IUserDirectory</c> lúc đọc. Không FK sang schema khác (Đ-2.2) — mọi id người dùng/đích là <c>uuid</c> trần.
///
/// Ghi (upsert gộp) đi bằng SQL thô trong một transaction ở D9, không qua ChangeTracker. Thuộc tính có setter là những cột
/// upsert/đánh dấu đã đọc được đổi.
/// </summary>
public sealed class UserNotification
{
    /// <summary>UUID v7 do app sinh (<c>Uuid7.New()</c>).</summary>
    public Guid Id { get; init; }

    public Guid RecipientId { get; init; }

    /// <summary>Một trong <see cref="NotificationTypes"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Dựng bằng <see cref="Domain.GroupKey"/> — không gõ tay ở chỗ khác.</summary>
    public required string GroupKey { get; init; }

    /// <summary>Một trong <see cref="NotificationTargetTypes"/>.</summary>
    public required string TargetType { get; init; }

    public Guid TargetId { get; init; }

    /// <summary>Bài chứa đích khi đích là bình luận — để FE dẫn tới bài.</summary>
    public Guid? PostId { get; init; }

    /// <summary>Người gây ra sự kiện mới nhất. <c>null</c> với <see cref="NotificationTypes.Moderation"/> — không lộ ai kiểm duyệt.</summary>
    public Guid? LastActorId { get; set; }

    /// <summary>Số người KHÁC NHAU trong đợt hiện tại (≥ 1). Đợt mới (nhóm đã đọc rồi có sự kiện) đếm lại từ 1.</summary>
    public int ActorCount { get; set; } = 1;

    /// <summary>Lý do ẩn — chỉ <see cref="NotificationTypes.Moderation"/>.</summary>
    public string? ReasonCode { get; init; }

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Lúc sự kiện mới nhất dồn vào nhóm — khóa sắp danh sách (<c>idx_notifications_recent</c>). ĐÁNH DẤU ĐÃ ĐỌC KHÔNG ĐỔI
    /// cột này: đổi thì đọc một thông báo cũ là nó nhảy lên đầu danh sách.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
