using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// C3 (GĐ4, Đ-4.9): MỘT chỗ dựng <see cref="PostResponse"/> cho mọi DANH SÁCH bài — trang cá nhân
/// (<c>GET /users/{id}/posts</c>) và feed. Số câu truy vấn cố định, không phụ thuộc số bài: một lô ảnh, một lô tác giả.
///
/// <b>GĐ3 thêm một lô <c>myReaction</c> VÀO ĐÂY</b> (giai-doan-4.md Mục 9.1 #1) — feed và trang cá nhân có trường mới cùng
/// lúc, không sửa dòng nào của feed. Thêm nó ở chỗ khác là hai danh sách lệch nhau.
///
/// Hydrator KHÔNG biết feed tồn tại: không nhận <c>FeedSources</c>, không kiểm BR-02. Lọc là việc của người gọi, TRƯỚC khi
/// gọi hàm này (truy vấn của trang cá nhân lọc trong SQL; feed kiểm lại ở <c>FeedService</c>). Nhận nguồn "cho tiện" là bắt
/// trang cá nhân bịa ra nguồn.
/// </summary>
public sealed class PostHydrator(IPostStore posts, IUserDirectory directory, PostResponseMapper mapper)
{
    /// <summary>
    /// Giữ nguyên thứ tự của <paramref name="page"/>. Trang rỗng → không câu truy vấn nào.
    /// </summary>
    /// <param name="actorId">Người xem — cho các trường theo người xem (<c>canEdit</c>; GĐ3 thêm <c>myReaction</c>).</param>
    public async Task<IReadOnlyList<PostResponse>> HydrateAsync(
        IReadOnlyList<Post> page, Guid actorId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Count == 0)
            return [];

        // Thứ tự hai câu giữ nguyên như PostReadService.ListByUserAsync trước khi tách (ảnh rồi tác giả): FEED-Q1 và mọi số đo
        // truy vấn đã ghi đều giả định thứ tự đó.
        var media = await posts.MediaOfAsync([.. page.Select(p => p.PostId)], ct);
        var cards = await directory.GetManyAsync([.. page.Select(p => p.AuthorId).Distinct()], ct);

        return mapper.ToResponses(page, media, cards, actorId);
    }
}
