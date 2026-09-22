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

    /// <summary>
    /// D3 — một câu <c>UPDATE</c> có điều kiện (Đ-4.14): cặp chuẩn hóa, <c>pending</c>,
    /// <c>requester_id = requesterId</c> (lời mời phải ĐẾN từ người kia). Không <c>SELECT</c> trước.
    /// Tự gán <c>accepted_at</c> + <c>updated_at</c>: <c>ExecuteUpdateAsync</c> bỏ qua <c>SaveChanges</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c> khi đúng một dòng đổi thành <c>accepted</c>; <c>false</c> khi 0 dòng — service dịch thành
    /// <b>403</b> cùng một phản hồi cho mọi lý do (không có lời mời, tự chấp nhận, đã là bạn, người thứ ba).
    /// </returns>
    Task<bool> AcceptIncomingAsync(FriendPair pair, Guid requesterId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// D4 — xóa lời mời <c>pending</c> của cặp, theo chiều nào cũng được (người gửi hủy hoặc người nhận từ chối).
    /// Vế <c>status = pending</c> bắt buộc: thiếu nó thì endpoint này xóa luôn tình bạn đã <c>accepted</c>.
    /// Không <c>SELECT</c> trước. <c>ExecuteDeleteAsync</c> không cần <c>updated_at</c> — dòng không còn.
    /// </summary>
    /// <returns><c>true</c> khi có dòng bị xóa; <c>false</c> khi 0 dòng. Service vẫn trả 204, và chỉ khi <c>true</c> mới xóa cache.</returns>
    Task<bool> DeletePendingAsync(FriendPair pair, CancellationToken ct);

    /// <summary>
    /// D4 — xóa quan hệ <c>accepted</c> của cặp. Vế trạng thái bắt buộc: thiếu nó thì hủy kết bạn xóa luôn lời mời đang chờ.
    /// Không <c>SELECT</c> trước.
    /// </summary>
    /// <returns><c>true</c> khi có dòng bị xóa; <c>false</c> khi 0 dòng. Service vẫn trả 204, và chỉ khi <c>true</c> mới xóa cache.</returns>
    Task<bool> DeleteAcceptedAsync(FriendPair pair, CancellationToken ct);

    /// <summary>
    /// D5 — một trang bạn <c>accepted</c> của <paramref name="me"/>, <c>accepted_at DESC</c> rồi id người kia DESC.
    /// <paramref name="take"/> là <c>limit + 1</c>. Keyset theo <paramref name="cursor"/>; <c>null</c> là trang đầu.
    /// </summary>
    Task<IReadOnlyList<FriendListRow>> ListFriendsAsync(
        Guid me, FriendCursor? cursor, int take, CancellationToken ct);

    /// <summary>
    /// D5 — một trang lời mời <c>pending</c> của <paramref name="me"/>. <paramref name="incoming"/>: người kia gửi
    /// (<c>requester_id ≠ me</c>); ngược lại là lời mình gửi. Sắp <c>created_at DESC</c>, hòa thì id người kia DESC.
    /// </summary>
    Task<IReadOnlyList<FriendListRow>> ListRequestsAsync(
        Guid me, bool incoming, FriendCursor? cursor, int take, CancellationToken ct);

}
