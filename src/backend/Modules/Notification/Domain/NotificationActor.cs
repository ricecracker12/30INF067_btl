namespace SocialApp.Modules.Notification.Domain;

/// <summary>
/// Một người đã góp vào ĐỢT hiện tại của một nhóm thông báo (bảng <c>notification.notification_actors</c>, PK cặp) — Đ-6.16.
///
/// Bảng này là thứ làm cho <see cref="UserNotification.ActorCount"/> đếm người KHÁC NHAU chứ không đếm lượt: Bình thả tim, gỡ,
/// thả lại vẫn chỉ là một dòng. Đợt mới thì D9 xóa sạch các dòng của nhóm rồi đếm lại.
///
/// FK tới <c>notifications</c> với <c>ON DELETE CASCADE</c> — cùng schema nên FK được giữ (Đ-2.2 chỉ cấm FK chéo schema).
/// <see cref="ActorId"/> là id người dùng của Identity: <c>uuid</c> trần.
/// </summary>
public sealed class NotificationActor
{
    public Guid NotificationId { get; init; }

    public Guid ActorId { get; init; }
}
