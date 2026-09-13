namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Mười bảy mã quyền của ma trận Mục 6.7.2, đúng thứ tự <c>permission_id</c> 1..17 ở Mục 5.2.
/// Dạng <c>resource.action</c>; <c>[RequirePermission("...")]</c> ở khối C bám vào chuỗi này.
///
/// Điều kiện <c>(own)</c> và <c>(bạn)</c> trong ma trận gốc KHÔNG có mặt ở đây: chúng không biểu
/// diễn được bằng một dòng <c>role_permissions</c>, mà là logic ownership ở tầng 3 (Mục 6.3).
/// </summary>
public static class PermissionCodes
{
    /// <summary>1 — đọc bài công khai.</summary>
    public const string PostReadPublic = "post.read.public";

    /// <summary>2 — đọc bài của bạn bè.</summary>
    public const string PostReadFriends = "post.read.friends";

    /// <summary>3 — đăng bài.</summary>
    public const string PostCreate = "post.create";

    /// <summary>4 — sửa bài.</summary>
    public const string PostUpdate = "post.update";

    /// <summary>5 — xóa bài.</summary>
    public const string PostDelete = "post.delete";

    /// <summary>6 — ẩn bài của người khác. USER không có; MODERATOR và ADMIN có.</summary>
    public const string PostHide = "post.hide";

    /// <summary>7 — bình luận.</summary>
    public const string CommentCreate = "comment.create";

    /// <summary>8 — thả cảm xúc.</summary>
    public const string ReactionSet = "reaction.set";

    /// <summary>9 — gửi lời mời kết bạn.</summary>
    public const string FriendRequest = "friend.request";

    /// <summary>10 — phản hồi lời mời kết bạn.</summary>
    public const string FriendRespond = "friend.respond";

    /// <summary>11 — nhắn tin.</summary>
    public const string MessageSend = "message.send";

    /// <summary>12 — gửi báo cáo vi phạm.</summary>
    public const string ReportCreate = "report.create";

    /// <summary>13 — xử lý báo cáo vi phạm. USER không có; MODERATOR và ADMIN có.</summary>
    public const string ReportResolve = "report.resolve";

    /// <summary>14 — khóa tài khoản.</summary>
    public const string UserLock = "user.lock";

    /// <summary>15 — mở khóa tài khoản.</summary>
    public const string UserUnlock = "user.unlock";

    /// <summary>16 — gán vai trò cho tài khoản.</summary>
    public const string RoleAssign = "role.assign";

    /// <summary>17 — đọc nhật ký kiểm toán.</summary>
    public const string AuditRead = "audit.read";

    /// <summary>
    /// Toàn bộ 17 mã, xếp theo đúng <c>permission_id</c> 1..17 của Mục 5.2 — chỉ số mảng cộng 1
    /// chính là id. Seeder (A4) và test ma trận AuthZ (B2) đọc danh sách này.
    /// </summary>
    public static readonly string[] All =
    [
        PostReadPublic,  // 1
        PostReadFriends, // 2
        PostCreate,      // 3
        PostUpdate,      // 4
        PostDelete,      // 5
        PostHide,        // 6
        CommentCreate,   // 7
        ReactionSet,     // 8
        FriendRequest,   // 9
        FriendRespond,   // 10
        MessageSend,     // 11
        ReportCreate,    // 12
        ReportResolve,   // 13
        UserLock,        // 14
        UserUnlock,      // 15
        RoleAssign,      // 16
        AuditRead,       // 17
    ];
}
