using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Notification.Domain;

/// <summary>
/// Loại đích mà thông báo dẫn tới (cột <c>target_type</c>, xem <see cref="MaxLength"/>) — FE dựng đường dẫn từ cặp
/// <c>(targetType, targetId)</c>, cộng <c>post_id</c> khi đích là bình luận.
///
/// Hai hàm <c>From</c> là phép dịch enum SharedKernel → chuỗi DB, đặt ở MỘT chỗ để handler D10 và <see cref="GroupKey"/>
/// không mỗi nơi tự viết thường tên enum. Nhận enum của SharedKernel, KHÔNG nhận enum của Content (ADR-001).
/// </summary>
public static class NotificationTargetTypes
{
    /// <summary>
    /// Độ dài cột <c>target_type</c> — varchar(20), KHÔNG phải varchar(10) như DDL Mục 4 bản đầu: <see cref="Conversation"/> dài
    /// 12 ký tự, varchar(10) chặn mọi thông báo <c>message</c> bằng <c>22001</c>. 20 cho cùng cỡ <c>audit_logs.target_type</c>.
    /// </summary>
    public const int MaxLength = 20;

    public const string Post = "post";

    public const string Comment = "comment";

    public const string User = "user";

    public const string Conversation = "conversation";

    public static readonly string[] All = [Post, Comment, User, Conversation];

    public static string From(ReactionTargetKind kind) => kind switch
    {
        ReactionTargetKind.Post => Post,
        ReactionTargetKind.Comment => Comment,
        // Thêm thành viên enum mà quên nhánh → ném, không sinh một chuỗi lạ làm khóa gộp lệch nhau.
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Loại đích cảm xúc chưa có chuỗi thông báo."),
    };

    public static string From(ModerationTargetType type) => type switch
    {
        ModerationTargetType.Post => Post,
        ModerationTargetType.Comment => Comment,
        ModerationTargetType.User => User,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Loại đối tượng kiểm duyệt chưa có chuỗi thông báo."),
    };
}
