namespace SocialApp.SharedKernel.Contracts;

/// <summary>
/// Tập tác giả đổ bài vào feed của một người (Đ-4.4, Đ-4.5). Hai tập RỜI NHAU: FollowingOnly = đang theo dõi
/// TRỪ bạn bè — để mỗi tác giả có đúng một mức nhìn. "Chính mình" KHÔNG nằm trong đây; FeedService tự thêm.
///
/// Khác <see cref="IFriendshipReader"/> ở chính sách tươi: hiện thực được phép cache ≤ 60s (C1), xóa bởi module chủ dữ liệu.
/// </summary>
public sealed record FeedSources(IReadOnlySet<Guid> Friends, IReadOnlySet<Guid> FollowingOnly)
{
    /// <summary>Điều kiện feed gợi ý (Đ-4.6): chưa có bạn và chưa theo dõi ai.</summary>
    public bool IsEmpty => Friends.Count == 0 && FollowingOnly.Count == 0;
}

/// <summary>
/// Nguồn feed mạng lưới của một người — chỉ đọc, batch (Đ-2.3 / Đ-4.4). Module Content không import SocialGraph.
/// </summary>
public interface IFeedSourceReader
{
    Task<FeedSources> GetAsync(Guid userId, CancellationToken ct = default);
}
