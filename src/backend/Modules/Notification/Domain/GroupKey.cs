using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Notification.Domain;

/// <summary>
/// Chỗ DUY NHẤT dựng <c>group_key</c> — khóa gộp của <c>uq_notifications_group (recipient_id, group_key)</c> mà upsert của D9
/// dựa vào (Đ-6.16, bảng Đ-6.17).
///
/// Một hàm cho MỖI loại, không hàm nào nhận event (L-A9 của hướng dẫn khối A+C): một <c>CommentCreated</c> sinh tới ba nhóm
/// (<c>comment</c> cho tác giả bài, <c>reply</c> cho tác giả bình luận cha, <c>tag</c> cho người được nhắc) — chọn nhóm là việc
/// của handler D10, dựng chuỗi là việc của lớp này.
///
/// <c>Guid</c> luôn định dạng <c>:D</c> — cùng bài học khóa Redis GĐ4 (<c>RedisFeedPageCache.Key</c>): hai chỗ gõ khóa khác
/// định dạng là gộp không bao giờ trúng, và không test một-chỗ nào bắt được. Tiền tố của mỗi khóa là đúng
/// <see cref="NotificationTypes"/> của nó.
/// </summary>
public static class GroupKey
{
    /// <summary>Độ dài cột <c>group_key</c> (varchar(120)). Khóa dài nhất hiện nay 55 ký tự (<c>moderation:comment:{id}</c>).</summary>
    public const int MaxLength = 120;

    /// <summary><c>comment:post:{postId}</c> — mọi bình luận trên một bài dồn vào một nhóm của tác giả bài.</summary>
    public static string Comment(Guid postId) =>
        $"{NotificationTypes.Comment}:{NotificationTargetTypes.Post}:{postId:D}";

    /// <summary><c>reply:comment:{parentCommentId}</c> — mọi trả lời một bình luận dồn vào một nhóm của tác giả bình luận đó.</summary>
    public static string Reply(Guid parentCommentId) =>
        $"{NotificationTypes.Reply}:{NotificationTargetTypes.Comment}:{parentCommentId:D}";

    /// <summary><c>reaction:{post|comment}:{targetId}</c> — cảm xúc trên bài và trên bình luận là hai nhóm khác nhau.</summary>
    public static string Reaction(ReactionTargetKind kind, Guid targetId) =>
        $"{NotificationTypes.Reaction}:{NotificationTargetTypes.From(kind)}:{targetId:D}";

    /// <summary><c>friend_request:{requesterId}</c> — mời lại sau khi hủy làm sáng lại đúng nhóm cũ.</summary>
    public static string FriendRequest(Guid requesterId) =>
        $"{NotificationTypes.FriendRequest}:{requesterId:D}";

    /// <summary><c>friend_accepted:{accepterId}</c>.</summary>
    public static string FriendAccepted(Guid accepterId) =>
        $"{NotificationTypes.FriendAccepted}:{accepterId:D}";

    /// <summary><c>message:{conversationId}</c> — mỗi cuộc trò chuyện một nhóm.</summary>
    public static string Message(Guid conversationId) =>
        $"{NotificationTypes.Message}:{conversationId:D}";

    /// <summary><c>moderation:{post|comment|user}:{targetId}</c>.</summary>
    public static string Moderation(ModerationTargetType type, Guid targetId) =>
        $"{NotificationTypes.Moderation}:{NotificationTargetTypes.From(type)}:{targetId:D}";

    /// <summary><c>tag:comment:{commentId}</c> — mỗi bình luận nhắc tên một nhóm.</summary>
    public static string Tag(Guid commentId) =>
        $"{NotificationTypes.Tag}:{NotificationTargetTypes.Comment}:{commentId:D}";
}
