namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Trạng thái bình luận — khớp đúng <c>ck_comments_status</c> (Mục 4):
/// <c>status IN ('visible','deleted')</c>. Xóa bình luận là xóa mềm để GIỮ NHÁNH trả lời bên dưới.
///
/// Khung của Đ-2.12: GĐ2 không có endpoint nào ghi giá trị này.
/// </summary>
public enum CommentStatus
{
    Visible,
    Deleted,
}
