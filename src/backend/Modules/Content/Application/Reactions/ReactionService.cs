using SocialApp.Modules.Content.Application.Comments;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Application.Reactions;

/// <summary>
/// D5 + D6 GĐ3 — thả / đổi / gỡ cảm xúc của người gọi (Đ-3.7). Mỗi hàm: BR-02 của bài chứa đối tượng (Đ-3.3) → khuôn giao dịch
/// Đ-3.8 ở <see cref="IReactionStore"/> → event SAU commit khi có cảm xúc mới hoặc đổi loại (Đ-3.12).
///
/// Cảm xúc KHÔNG đòi hồ sơ (Mục 6.1): nó không hiển thị tác giả ở đâu cả.
/// </summary>
public sealed class ReactionService(
    PostAccess access,
    ICommentStore comments,
    IReactionStore store,
    ContentInteractionEvents events,
    TimeProvider clock)
{
    /// <summary><c>PUT</c> (<paramref name="desired"/> khác null) hoặc <c>DELETE</c> (null) <c>/posts/{postId}/reactions/me</c>.</summary>
    public async Task<Result<ReactionSummary>> ApplyToPostAsync(
        Guid postId, Guid actorId, ReactionType? desired, CancellationToken ct)
    {
        var post = await access.ResolveVisibleAsync(postId, actorId, ct);
        if (post is null)
            return ContentErrors.PostNotFound;

        var applied = await store.ApplyAsync(ReactionTargetType.Post, postId, actorId, desired, clock.GetUtcNow(), ct);
        if (applied is null)
            return ContentErrors.PostNotFound;   // bài vừa bị xóa/ẩn giữa lúc kiểm BR-02 và lúc khóa

        Announce(applied, ReactionTargetType.Post, postId, postId, post.AuthorId, actorId);
        return new ReactionSummary(applied.Counts, desired);
    }

    /// <summary>
    /// <c>PUT</c>/<c>DELETE /comments/{commentId}/reactions/me</c>. Bình luận không có · không còn visible · nằm trong bài không
    /// xem được → CÙNG <see cref="ContentErrors.CommentNotFound"/> (<c>READ-REACT-02</c>, <c>REACT-06</c>).
    /// </summary>
    public async Task<Result<ReactionSummary>> ApplyToCommentAsync(
        Guid commentId, Guid actorId, ReactionType? desired, CancellationToken ct)
    {
        var comment = await comments.FindAsync(commentId, ct);
        if (comment is null || comment.Status != CommentStatus.Visible)
            return ContentErrors.CommentNotFound;

        if (await access.ResolveVisibleAsync(comment.PostId, actorId, ct) is null)
            return ContentErrors.CommentNotFound;

        var applied = await store.ApplyAsync(ReactionTargetType.Comment, commentId, actorId, desired, clock.GetUtcNow(), ct);
        if (applied is null)
            return ContentErrors.CommentNotFound;   // bình luận vừa bị xóa giữa lúc kiểm và lúc khóa

        Announce(applied, ReactionTargetType.Comment, commentId, comment.PostId, comment.AuthorId, actorId);
        return new ReactionSummary(applied.Counts, desired);
    }

    /// <summary>Chỉ INSERT (thả mới) và UPDATE (đổi loại) có gì để thông báo; DELETE và "giống nhau" thì không.</summary>
    private void Announce(
        ReactionApplied applied, ReactionTargetType type, Guid targetId, Guid postId, Guid targetAuthorId, Guid actorId)
    {
        if (applied.Change.Write is ReactionWrite.Insert or ReactionWrite.Update)
            events.ReactionSet(type, targetId, postId, targetAuthorId, actorId, isNew: applied.Change.Write == ReactionWrite.Insert);
    }
}
