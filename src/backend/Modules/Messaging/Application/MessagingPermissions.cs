namespace SocialApp.Modules.Messaging.Application;

/// <summary>
/// Mã quyền tầng 2 mà module Messaging dùng, dưới dạng hằng chuỗi CỦA CHÍNH MODULE (khuôn <c>SocialGraphPermissions</c>).
/// Không <c>using</c> <c>PermissionCodes</c> của Identity: luật 4 cấm import chéo module (<c>ModuleBoundaryTests</c>).
///
/// Cái giá là gõ sai không bị compiler bắt, và Admin VẪN QUA (short-circuit của <c>PermissionChecks</c> không nhìn mã) — người
/// kiểm tay bằng Admin thấy "chạy tốt" trong khi USER bị chặn vĩnh viễn. Lưới: <c>MessagingPermissionsTests</c> ở
/// ArchitectureTests khẳng định mọi hằng ở đây nằm trong <c>PermissionCodes.All</c>.
///
/// <c>message.send</c> (mã 11/17 của GĐ1, đã gán USER + MODERATOR) canh tầng 2 của CẢ HAI cửa gửi tin — REST bằng
/// <c>[RequirePermission]</c>, hub bằng <c>IPermissionCache</c> trong service (Đ-5.7) — và của tạo hội thoại. Đọc hội thoại,
/// lịch sử, biên nhận chỉ cần <c>[Authorize]</c> (Mục 6.1).
/// </summary>
public static class MessagingPermissions
{
    /// <summary>Gửi tin (REST + hub) và mở hội thoại mới (<c>POST /conversations</c>).</summary>
    public const string MessageSend = "message.send";

    /// <summary>Mọi hằng, cho test kiến trúc đọc — thêm hằng mới thì thêm vào đây, nếu không nó không được canh.</summary>
    public static readonly string[] All = [MessageSend];
}
