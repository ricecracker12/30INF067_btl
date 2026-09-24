using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// D0 GĐ3 (Đ-3.3): MỘT hàm trả lời "người gọi có được thấy bài này — và mọi thứ treo dưới nó — không". Bình luận và cảm xúc
/// không có mức riêng tư riêng: chúng hiện khi và chỉ khi bài chứa chúng hiện, đánh giá lại ở MỌI request. Mọi endpoint của GĐ3
/// đi qua đây; endpoint nhận <c>commentId</c> tra <c>post_id</c> của bình luận trước rồi mới gọi. Hai bản của cùng một luật là
/// có chỗ lệch (LEAK-01).
///
/// Chặt hơn <c>GET /posts/{id}</c> ở một điểm, có chủ đích: bài phải <c>published</c> — bài <c>hidden</c> (GĐ6) không nhận bình
/// luận hay cảm xúc mới và không lộ bình luận, kể cả với tác giả (Đ-3.3 "hai trường hợp biên").
/// </summary>
public sealed class PostAccess(IPostStore posts, IFriendshipReader friends)
{
    /// <returns>Bài nếu người gọi xem được; <c>null</c> cho mọi lý do trượt — không tồn tại, đã xóa, bị ẩn, không đạt BR-02.</returns>
    public async Task<Post?> ResolveVisibleAsync(Guid postId, Guid actorId, CancellationToken ct)
    {
        var post = await posts.FindAsync(postId, ct);
        if (post is null || post.Status != PostStatus.Published)
            return null;

        // Chỉ hỏi SocialGraph khi luật cần — cùng lý do PostReadService.GetAsync: bài public/private không phụ thuộc quan hệ.
        var areFriends = post.Privacy == PostPrivacy.Friends
            && post.AuthorId != actorId
            && await friends.AreFriendsAsync(actorId, post.AuthorId, ct);

        return PostVisibility.CanView(post.Privacy, post.AuthorId, actorId, areFriends) ? post : null;
    }
}
