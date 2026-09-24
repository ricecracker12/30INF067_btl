namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Trạng thái bình luận — khớp đúng <c>ck_comments_status</c>: <c>status IN ('visible','deleted','hidden')</c>.
/// Xóa bình luận là xóa mềm để GIỮ NHÁNH trả lời bên dưới (Đ-3.5).
///
/// <see cref="Hidden"/> có mặt từ migration đầu tiên của GĐ3 theo thỏa thuận Đ-6.14: GĐ6 (kiểm duyệt) ẩn bình luận
/// vi phạm mà không phải ALTER CHECK trên bảng của Content. GĐ3 không có đường nào ghi giá trị này; mapper coi nó như
/// <see cref="Deleted"/> (không lộ <c>body</c>/<c>author</c>).
/// </summary>
public enum CommentStatus
{
    Visible,
    Deleted,
    Hidden,
}
