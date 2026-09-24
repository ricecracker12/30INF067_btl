using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Content.Application;

/// <summary>
/// Event của GĐ3 sau <c>COMMIT</c> (Đ-3.12), phát qua <see cref="IEventPublisher"/> của GĐ6 (Đ-6.2, Đ-6.4) — cùng khuôn
/// <c>SocialGraphEvents</c>. GĐ6 nối thông báo <c>comment</c>/<c>reply</c>/<c>reaction</c> vào hai record này; người nhận nằm sẵn
/// trong event để handler không phải đọc bảng của Content.
///
/// <b>Luôn gọi SAU <c>COMMIT</c></b>, không bao giờ trong transaction: phát trong transaction thì handler có thể chạy trước khi dữ
/// liệu nhìn thấy được, hoặc chạy cho một bình luận đã rollback. <c>Publish</c> không chờ handler và không ném.
/// </summary>
public sealed class ContentInteractionEvents(IEventPublisher publisher)
{
    /// <summary>Bình luận / phản hồi vừa tạo (D3).</summary>
    /// <param name="parent">Bình luận cha khi là phản hồi — cho thông báo <c>reply</c> tới tác giả của nó.</param>
    public void CommentCreated(Comment comment, Guid postAuthorId, Comment? parent) =>
        publisher.Publish(new SharedKernel.Events.CommentCreated(
            comment.CommentId,
            comment.PostId,
            postAuthorId,
            parent?.CommentId,
            parent?.AuthorId,
            comment.AuthorId,
            MentionedUserIds: []));   // @tag là việc của GĐ6 (Mục 2)

    /// <summary>
    /// Cảm xúc vừa thả (<paramref name="isNew"/>) hoặc đổi loại. Gỡ cảm xúc và "giống nhau" KHÔNG gọi hàm này — không có gì để
    /// thông báo.
    /// </summary>
    public void ReactionSet(ReactionTargetType targetType, Guid targetId, Guid postId, Guid targetAuthorId, Guid actorId, bool isNew) =>
        publisher.Publish(new SharedKernel.Events.ReactionSet(
            targetType switch
            {
                ReactionTargetType.Post => ReactionTargetKind.Post,
                ReactionTargetType.Comment => ReactionTargetKind.Comment,
                _ => throw new ArgumentOutOfRangeException(nameof(targetType)),
            },
            targetId,
            postId,
            targetAuthorId,
            actorId,
            isNew));
}
