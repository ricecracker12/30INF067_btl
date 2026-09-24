using Microsoft.Extensions.Logging;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Application.Comments;

/// <summary>
/// Entity → <see cref="CommentResponse"/>. MỘT chỗ cho mọi đường trả bình luận (D1, D2, D3), và là chỗ DUY NHẤT xóa
/// <c>author</c>/<c>body</c> của bình luận không còn <c>visible</c> (Mục 8.1): xóa ở truy vấn thì sẽ có một đường đọc quên.
/// </summary>
public sealed class CommentResponseMapper(IObjectStorage storage, ILogger<CommentResponseMapper> logger)
{
    private static readonly IReadOnlyDictionary<string, int> NoReactions = new Dictionary<string, int>();

    /// <param name="cards">Kết quả của MỘT lời gọi <c>IUserDirectory.GetManyAsync</c> cho cả trang (Đ-2.3).</param>
    /// <param name="mine">Kết quả của MỘT lời gọi <see cref="Reactions.IReactionReader.GetMineAsync"/> cho cả trang (Đ-3.11).</param>
    public IReadOnlyList<CommentResponse> ToResponses(
        IReadOnlyList<Comment> comments,
        IReadOnlyDictionary<Guid, UserCard> cards,
        IReadOnlyDictionary<Guid, ReactionType> mine,
        Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(mine);

        return [.. comments.Select(c => ToResponse(
            c, cards.GetValueOrDefault(c.AuthorId), mine.TryGetValue(c.CommentId, out var r) ? r : null, actorId))];
    }

    public CommentResponse ToResponse(Comment comment, UserCard? author, ReactionType? myReaction, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(comment);

        // Đ-3.5 + Đ-6.14: "đã xóa" và "bị ẩn" giữ chỗ và nhánh, nhưng không lộ ai viết, viết gì, hay ai đã thả cảm xúc.
        if (comment.Status != CommentStatus.Visible)
            return new CommentResponse(
                comment.CommentId, comment.PostId, comment.ParentId, comment.Depth, comment.Status,
                Author: null, Body: null, comment.ReplyCount, NoReactions, MyReaction: null, comment.CreatedAt,
                CanDelete: false);

        if (author is null)
            // CHỈ commentId (Mục 1.3 luật 9) — cùng lý do PostResponseMapper.
            logger.LogWarning("Bình luận {CommentId} không tra được tác giả trong IUserDirectory", comment.CommentId);

        return new CommentResponse(
            comment.CommentId,
            comment.PostId,
            comment.ParentId,
            comment.Depth,
            comment.Status,
            new PostAuthor(
                comment.AuthorId,
                author?.DisplayName ?? PostResponseMapper.UnknownAuthorName,
                author?.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null),
            comment.Body,
            comment.ReplyCount,
            comment.ReactionCounts,
            myReaction,
            comment.CreatedAt,
            CanDelete: comment.AuthorId == actorId);
    }
}
