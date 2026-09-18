namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Chủ sở hữu của một ảnh — khớp đúng <c>ck_media_owner_type</c> (Mục 4):
/// <c>owner_type IN ('post','message')</c>.
///
/// GĐ2 chỉ ghi <see cref="Post"/>, nhưng CHECK nhận cả hai ngay từ đầu để GĐ5 (tin nhắn có ảnh) không
/// phải đổi ràng buộc (Đ-2.12). Đây cũng là lý do <c>media_attachments</c> KHÔNG có khóa ngoại tới
/// <c>posts</c>: bảng đa hình thì FK trỏ đi đâu cũng sai một nửa.
/// </summary>
public enum MediaOwnerType
{
    /// <summary>Ảnh của một bài đăng — <c>owner_id</c> là <c>post_id</c>.</summary>
    Post,

    /// <summary>Ảnh của một tin nhắn (GĐ5) — <c>owner_id</c> là <c>message_id</c>.</summary>
    Message,
}
