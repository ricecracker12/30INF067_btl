using SocialApp.Modules.Content.Application.Reactions;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// C3 (GĐ4, Đ-4.9): MỘT chỗ dựng <see cref="PostResponse"/> cho mọi DANH SÁCH bài — trang cá nhân
/// (<c>GET /users/{id}/posts</c>) và feed. Số câu truy vấn cố định, không phụ thuộc số bài: một lô ảnh, một lô tác giả, một lô
/// <c>myReaction</c> (GĐ3).
///
/// <b>GĐ3 thêm lô <c>myReaction</c> VÀO ĐÂY</b> (giai-doan-4.md Mục 9.1 #1, Đ-3.11) — feed và trang cá nhân có trường mới cùng
/// lúc, không sửa dòng nào của feed. Thêm nó ở chỗ khác là hai danh sách lệch nhau — và lọt vào cache feed là người B thấy nút
/// tim sáng theo cảm xúc của người A (GĐ4-01).
///
/// Hydrator KHÔNG biết feed tồn tại: không nhận <c>FeedSources</c>, không kiểm BR-02. Lọc là việc của người gọi, TRƯỚC khi
/// gọi hàm này (truy vấn của trang cá nhân lọc trong SQL; feed kiểm lại ở <c>FeedService</c>). Nhận nguồn "cho tiện" là bắt
/// trang cá nhân bịa ra nguồn.
/// </summary>
public sealed class PostHydrator(
    IPostStore posts, IUserDirectory directory, IReactionReader reactions, PostResponseMapper mapper)
{
    /// <summary>
    /// Giữ nguyên thứ tự của <paramref name="page"/>. Trang rỗng → không câu truy vấn nào.
    /// </summary>
    /// <param name="actorId">Người xem — cho các trường theo người xem (<c>canEdit</c>, <c>myReaction</c>).</param>
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
        // GĐ3: câu thứ ba, cuối cùng — mọi số đo FEED-Q1 cộng đúng một câu, không đổi thứ tự hai câu trước.
        var mine = await reactions.GetMineAsync(actorId, ReactionTargetType.Post, [.. page.Select(p => p.PostId)], ct);

        return mapper.ToResponses(page, media, cards, mine, actorId);
    }
}
