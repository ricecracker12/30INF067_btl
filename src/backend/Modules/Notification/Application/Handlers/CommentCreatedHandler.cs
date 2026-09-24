using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Notification.Application.Handlers;

/// <summary>
/// <c>comment</c> + <c>reply</c> (Đ-6.17) từ MỘT <c>CommentCreated</c> — một event sinh nhiều nhóm, nên chọn nhóm ở đây, dựng chuỗi ở
/// <see cref="GroupKey"/> (L-A9).
/// <list type="bullet">
/// <item><c>reply</c> → tác giả bình luận cha, nhóm <c>reply:comment:{parentId}</c>, đích là bình luận cha (FE mở đúng nhánh).</item>
/// <item><c>comment</c> → tác giả bài, nhóm <c>comment:post:{postId}</c>, đích là bài. KHÔNG gửi khi tác giả bài cũng là tác giả bình luận
/// cha: người đó đã nhận <c>reply</c> cho chính bình luận này — hai chuông cho một câu trả lời là thừa.</item>
/// </list>
/// Tự báo mình (người trả lời chính bình luận của mình, tác giả bài tự bình luận) → store bỏ qua (D9). <c>tag</c> KHÔNG làm: cắt theo B.10
/// thứ tự 1 — <c>MentionedUserIds</c> luôn rỗng (Content chưa có cú pháp nhắc tên).
///
/// Không bắt ngoại lệ — xem <see cref="FriendRequestSentHandler"/>. Hai lần upsert là hai transaction: lần đầu hỏng thì lần sau không
/// chạy và bus ghi log; không có gì phải cùng số phận giữa hai người nhận khác nhau.
/// </summary>
public sealed class CommentCreatedHandler(INotificationStore store) : IIntegrationEventHandler<CommentCreated>
{
    public async Task HandleAsync(CommentCreated integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        foreach (var upsert in For(integrationEvent))
            await store.UpsertAsync(upsert, ct);
    }

    public static IReadOnlyList<NotificationUpsert> For(CommentCreated e)
    {
        var upserts = new List<NotificationUpsert>(2);

        if (e.ParentCommentId is { } parentId && e.ParentAuthorId is { } parentAuthorId)
            upserts.Add(new NotificationUpsert(
                RecipientId: parentAuthorId,
                Type: NotificationTypes.Reply,
                GroupKey: GroupKey.Reply(parentId),
                TargetType: NotificationTargetTypes.Comment,
                TargetId: parentId,
                PostId: e.PostId,
                ActorId: e.ActorId,
                ReasonCode: null));

        if (e.ParentAuthorId != e.PostAuthorId)
            upserts.Add(new NotificationUpsert(
                RecipientId: e.PostAuthorId,
                Type: NotificationTypes.Comment,
                GroupKey: GroupKey.Comment(e.PostId),
                TargetType: NotificationTargetTypes.Post,
                TargetId: e.PostId,
                PostId: e.PostId,
                ActorId: e.ActorId,
                ReasonCode: null));

        return upserts;
    }
}
