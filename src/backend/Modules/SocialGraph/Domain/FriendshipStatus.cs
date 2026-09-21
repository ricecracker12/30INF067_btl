namespace SocialApp.Modules.SocialGraph.Domain;

/// <summary>
/// Trạng thái quan hệ bạn bè — khớp <c>ck_friendships_status</c>: <c>pending</c> | <c>accepted</c>.
/// Chỉ hai giá trị: từ chối / hủy là xóa dòng (FR-011, Đ-4.14), không phải chuyển trạng thái.
/// </summary>
public enum FriendshipStatus
{
    Pending,
    Accepted,
}
