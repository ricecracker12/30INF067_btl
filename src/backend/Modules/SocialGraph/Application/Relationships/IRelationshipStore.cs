using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Bảng <c>socialgraph.friendships</c> + <c>socialgraph.follows</c> cho các luồng của khối D. Hiện thực EF nằm ở
/// <c>Infrastructure/Persistence</c> — <c>Application</c> không chạm EF (<c>PersistenceBoundaryTests</c> canh bằng máy).
///
/// Lớn dần theo từng <c>D*</c>, không khai trước thứ chưa có người gọi — cùng nếp <c>IPostStore</c>.
/// </summary>
public interface IRelationshipStore
{
    /// <summary>
    /// Dòng <c>friendships</c> của cặp chuẩn hóa, hoặc <c>null</c> khi chưa có quan hệ. <c>AsNoTracking</c>: đây là
    /// đường đọc (D1); D2–D4 thêm hàm ghi riêng chứ không thêm cờ <c>tracked</c> vào đây.
    /// </summary>
    Task<Friendship?> FindFriendshipAsync(FriendPair pair, CancellationToken ct);

    /// <summary>
    /// Người gọi (<paramref name="followerId"/>) có đang theo dõi <paramref name="followeeId"/> không. Tra PK cặp có
    /// hướng — một dòng hoặc không. Độc lập với <see cref="FindFriendshipAsync"/> (Đ-4.5).
    /// </summary>
    Task<bool> IsFollowingAsync(Guid followerId, Guid followeeId, CancellationToken ct);
}
