namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Đối tượng của một cảm xúc trong event. Enum RIÊNG của SharedKernel — không dùng
/// <c>SocialApp.Modules.Content.Domain.ReactionTargetType</c> (SharedKernel không tham chiếu module, ADR-001). Tên khác có chủ
/// đích: trùng tên thì file nào <c>using</c> cả hai namespace là <c>CS0104</c>. Content ánh xạ bằng một <c>switch</c>.
/// </summary>
public enum ReactionTargetKind
{
    Post,
    Comment,
}

/// <summary>
/// Phát bởi Content (A — GĐ3) sau <c>COMMIT</c> của tạo bình luận / trả lời (Đ-3.12). GĐ6 tiêu thụ: thông báo <c>comment</c>
/// cho <paramref name="PostAuthorId"/>, <c>reply</c> cho <paramref name="ParentAuthorId"/> khi có, <c>tag</c> cho
/// <paramref name="MentionedUserIds"/> (Đ-6.17). Người nhận nằm sẵn trong event để handler không phải đọc bảng của Content.
/// </summary>
/// <param name="ParentCommentId">Khác <c>null</c> khi là trả lời.</param>
/// <param name="ParentAuthorId">Tác giả bình luận cha; khác <c>null</c> đúng khi <paramref name="ParentCommentId"/> khác <c>null</c>.</param>
/// <param name="MentionedUserIds">Rỗng tới khi làm loại <c>tag</c> (B.10 thứ tự cắt 1).</param>
public sealed record CommentCreated(
    Guid CommentId,
    Guid PostId,
    Guid PostAuthorId,
    Guid? ParentCommentId,
    Guid? ParentAuthorId,
    Guid ActorId,
    IReadOnlyList<Guid> MentionedUserIds) : IIntegrationEvent;

/// <summary>
/// Phát bởi Content (A — GĐ3) sau <c>COMMIT</c> của thả / đổi cảm xúc (Đ-3.12). GĐ6 tiêu thụ: thông báo <c>reaction</c> cho
/// <paramref name="TargetAuthorId"/> (Đ-6.17).
/// </summary>
/// <param name="PostId">Bài chứa đối tượng — bằng <paramref name="TargetId"/> khi đối tượng là bài; dùng để điều hướng.</param>
/// <param name="IsNew"><c>true</c> khi người dùng chưa có cảm xúc nào trên đối tượng; <c>false</c> khi chỉ ĐỔI loại — handler
/// không tạo thông báo cho trường hợp này.</param>
public sealed record ReactionSet(
    ReactionTargetKind TargetType,
    Guid TargetId,
    Guid PostId,
    Guid TargetAuthorId,
    Guid ActorId,
    bool IsNew) : IIntegrationEvent;
