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

    /// <summary>
    /// INSERT lời mời pending. Không <c>SELECT</c> trước: PK cặp trả lời "đã có quan hệ" (Đ-4.14).
    /// </summary>
    /// <returns>
    /// <c>true</c> khi ghi xong; <c>false</c> khi đụng PK <c>PK_friendships</c> (23505) — đã có lời mời theo bất kỳ
    /// chiều nào hoặc đã là bạn. Service dịch thành <b>409</b>. Mọi lỗi DB khác PHẢI ném ra ngoài thành 500: nuốt
    /// chúng thành <c>false</c> là báo "đã có quan hệ" khi sự thật là hạ tầng hỏng.
    /// </returns>
    Task<bool> AddRequestAsync(Friendship friendship, CancellationToken ct);
}
