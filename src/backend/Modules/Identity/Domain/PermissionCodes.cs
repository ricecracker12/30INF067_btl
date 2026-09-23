namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Mười bảy mã quyền của ma trận Mục 6.7.2, đúng thứ tự <c>permission_id</c> 1..17 ở Mục 5.2, cộng mã 18 <see cref="RoleManage"/>
/// của GĐ6 (Đ-6.9 — lệch ma trận PTTK có chủ đích). Dạng <c>resource.action</c>; <c>[RequirePermission("...")]</c> bám vào chuỗi này.
///
/// Mã mới luôn thêm vào CUỐI <see cref="All"/>: id = vị trí + 1, nên chèn giữa là đổi id của mọi mã phía sau trên DB đã seed.
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
    /// 18 — định nghĩa vai trò: tạo, đổi tên, sửa tập quyền, xóa (GĐ6 Đ-6.9). Khác bậc với <see cref="RoleAssign"/> (gán người
    /// vào vai trò có sẵn). Chỉ ADMIN có — qua short-circuit, không dòng <c>role_permissions</c> nào; KHÔNG vào bộ bootstrap
    /// của USER/MODERATOR trong seeder.
    /// </summary>
    public const string RoleManage = "role.manage";

    /// <summary>
    /// Toàn bộ 18 mã, xếp theo đúng <c>permission_id</c> — chỉ số mảng cộng 1 chính là id (1..17 của Mục 5.2 GĐ1, 18 của
    /// Đ-6.9 GĐ6). Seeder và test ma trận AuthZ đọc danh sách này.
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
        RoleManage,      // 18 — GĐ6
    ];

    /// <summary>
    /// Mô tả tiếng Việt của từng mã, cho màn vai trò + ma trận quyền (<c>GET /admin/permissions</c>). Cột
    /// <c>permissions.description</c> là <c>varchar(120)</c>.
    ///
    /// Đi hai đường (lệch B.4 A3 của GĐ6, chốt 2026-09-23 — L-A2): seeder chèn kèm mô tả cho DB MỚI (<c>DO NOTHING</c> — không
    /// bao giờ ghi đè dòng đã có); migration <c>SystemRoleGuardAndPermissionDescriptions</c> điền cho DB đã seed từ GĐ1. Chỉ
    /// migration thì không đủ: <c>MigrateIdentityModuleAsync</c> chạy migrate TRƯỚC seed, nên trên DB mới câu <c>UPDATE</c> chạm 0
    /// dòng. Sửa câu mô tả sau này là migration mới — sửa ở đây chỉ tác động DB tạo sau đó.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PostReadPublic] = "Xem bài viết công khai",
        [PostReadFriends] = "Xem bài viết chỉ dành cho bạn bè",
        [PostCreate] = "Đăng bài viết",
        [PostUpdate] = "Sửa bài viết của mình",
        [PostDelete] = "Xóa bài viết của mình",
        [PostHide] = "Ẩn và khôi phục nội dung vi phạm của người khác",
        [CommentCreate] = "Bình luận",
        [ReactionSet] = "Bày tỏ cảm xúc",
        [FriendRequest] = "Gửi lời mời kết bạn và theo dõi",
        [FriendRespond] = "Chấp nhận lời mời kết bạn",
        [MessageSend] = "Nhắn tin",
        [ReportCreate] = "Báo cáo nội dung hoặc tài khoản vi phạm",
        [ReportResolve] = "Xem hàng đợi và xử lý báo cáo vi phạm",
        [UserLock] = "Khóa tài khoản",
        [UserUnlock] = "Mở khóa tài khoản",
        [RoleAssign] = "Gán vai trò cho tài khoản",
        [AuditRead] = "Xem nhật ký kiểm toán toàn hệ thống",
        [RoleManage] = "Tạo, đổi tên, sửa quyền và xóa vai trò",
    };
}
