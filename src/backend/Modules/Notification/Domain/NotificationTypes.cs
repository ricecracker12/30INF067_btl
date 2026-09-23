namespace SocialApp.Modules.Notification.Domain;

/// <summary>
/// Tám loại thông báo của Đ-6.17 dưới dạng chuỗi DB/hợp đồng — khớp <c>ck_notifications_type</c> (CHECK dựng từ
/// <see cref="All"/>) và trường <c>type</c> của <c>NotificationResponse</c> (notification-v1).
///
/// <c>const string</c> như <c>UserStatus</c>, không enum + converter: cột là <c>varchar</c> có CHECK và chuỗi đi thẳng ra
/// hợp đồng API. Mỗi loại cũng là TIỀN TỐ của <c>group_key</c> tương ứng (<see cref="GroupKey"/>).
/// </summary>
public static class NotificationTypes
{
    /// <summary>Độ dài cột <c>type</c> (varchar(20)).</summary>
    public const int MaxLength = 20;

    /// <summary>Có người bình luận bài của mình — nhận: tác giả bài.</summary>
    public const string Comment = "comment";

    /// <summary>Có người trả lời bình luận của mình — nhận: tác giả bình luận cha.</summary>
    public const string Reply = "reply";

    /// <summary>Có người bày tỏ cảm xúc MỚI (không phải đổi loại) — nhận: tác giả bài / bình luận.</summary>
    public const string Reaction = "reaction";

    /// <summary>Lời mời kết bạn — nhận: người được mời.</summary>
    public const string FriendRequest = "friend_request";

    /// <summary>Lời mời được chấp nhận — nhận: người đã gửi lời mời.</summary>
    public const string FriendAccepted = "friend_accepted";

    /// <summary>Tin nhắn mới khi người nhận offline (GĐ5 presence) — nhận: người nhận tin.</summary>
    public const string Message = "message";

    /// <summary>Nội dung của mình bị ẩn — nhận: tác giả. Không lộ ai kiểm duyệt (<c>last_actor_id</c> NULL).</summary>
    public const string Moderation = "moderation";

    /// <summary>Được nhắc tên trong bình luận — nhận: người được nhắc. Cắt được (B.10).</summary>
    public const string Tag = "tag";

    /// <summary>Tám giá trị hợp lệ — CHECK dựng từ mảng này.</summary>
    public static readonly string[] All =
        [Comment, Reply, Reaction, FriendRequest, FriendAccepted, Message, Moderation, Tag];
}
