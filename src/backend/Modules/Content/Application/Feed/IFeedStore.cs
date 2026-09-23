using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Hai truy vấn của feed (C2, GĐ4). Hiện thực bằng SQL thô tham số hóa ở <c>Infrastructure/Persistence</c> — cùng chỗ với
/// <see cref="IPostStore"/>; <c>Application</c> không chạm EF (<c>PersistenceBoundaryTests</c> canh).
///
/// Cả hai trả nguyên dòng <see cref="Post"/> đã sắp <c>(created_at DESC, post_id DESC)</c>, tối đa <c>take</c> dòng. Người gọi
/// truyền <c>take = limit + 1</c>: dòng thừa chỉ để biết còn trang sau, không đi vào phản hồi — cùng luật với
/// <see cref="IPostStore.ListByAuthorAsync"/>.
/// </summary>
public interface IFeedStore
{
    /// <summary>
    /// Đ-4.7. Một <c>LATERAL</c> mỗi nguồn trên <c>idx_posts_author_created</c>, gộp lấy <paramref name="take"/> dòng.
    /// Chi phí chặn trên bởi <c>số nguồn × take</c> dòng đọc qua index, không phụ thuộc tổng số bài.
    ///
    /// <b>BR-02 và BR-07 nằm TRONG truy vấn</b> (bảng Đ-4.5: chính mình thấy mọi mức, bạn bè <c>public</c> + <c>friends</c>,
    /// chỉ theo dõi <c>public</c>; chỉ <c>published</c>). Lọc sau khi đã lấy đủ dòng là trang ngắn ngẫu nhiên.
    /// <see cref="FeedVisibility.CanSee"/> là bản thứ hai của cùng luật — <c>FeedStoreTests</c> đối chiếu hai bản.
    /// </summary>
    Task<IReadOnlyList<Post>> NetworkPageAsync(
        Guid me, FeedSources sources, PostCursor? cursor, int take, CancellationToken ct);

    /// <summary>
    /// Đ-4.6 (sửa 2026-09-23). Bài <c>public</c> + <c>published</c> mới nhất của người khác (<c>idx_posts_public_recent</c>),
    /// CỘNG bài <c>published</c> của chính mình mọi mức (<c>idx_posts_author_created</c>). Chỉ gọi khi nguồn rỗng — có dù chỉ
    /// một kết nối là feed mạng lưới.
    /// </summary>
    Task<IReadOnlyList<Post>> SuggestedPageAsync(Guid me, PostCursor? cursor, int take, CancellationToken ct);
}
