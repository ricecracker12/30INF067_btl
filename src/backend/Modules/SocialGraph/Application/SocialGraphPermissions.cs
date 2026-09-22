namespace SocialApp.Modules.SocialGraph.Application;

/// <summary>
/// Hai mã quyền tầng 2 mà module SocialGraph dùng, dưới dạng hằng chuỗi CỦA CHÍNH MODULE.
///
/// Vì sao không <c>using SocialApp.Modules.Identity.Domain.PermissionCodes</c>: luật 4 cấm import chéo module
/// (<c>ModuleBoundaryTests</c> canh). Mã quyền là chuỗi trong HỢP ĐỒNG giữa hai module — SocialGraph bám vào
/// chuỗi, không bám vào kiểu của Identity.
///
/// Cái giá là gõ sai không bị compiler bắt, và Admin VẪN QUA (short-circuit của <c>PermissionHandler</c> không
/// nhìn mã) nên người test tay bằng Admin thấy "chạy tốt" trong khi USER bị chặn vĩnh viễn. Lưới cho đúng chuyện
/// đó: <c>SocialGraphPermissionsTests</c> ở ArchitectureTests — project test tham chiếu được cả hai module —
/// khẳng định mọi hằng ở đây nằm trong <c>PermissionCodes.All</c>.
///
/// Đ-4.12: theo dõi dùng <see cref="FriendRequest"/>, không thêm mã thứ 18. Hủy lời mời / từ chối / hủy kết bạn /
/// bỏ theo dõi chỉ <c>[Authorize]</c> — không có hằng ở đây.
/// </summary>
public static class SocialGraphPermissions
{
    /// <summary>Gửi lời mời kết bạn (<c>POST /friends/requests</c>) và theo dõi (<c>PUT /follows/{userId}</c>).</summary>
    public const string FriendRequest = "friend.request";

    /// <summary>Chấp nhận lời mời (<c>POST /friends/requests/{userId}/accept</c>).</summary>
    public const string FriendRespond = "friend.respond";

    /// <summary>Cả hai mã, cho test kiến trúc đọc — thêm hằng mới thì thêm vào đây, nếu không nó không được canh.</summary>
    public static readonly string[] All = [FriendRequest, FriendRespond];
}
