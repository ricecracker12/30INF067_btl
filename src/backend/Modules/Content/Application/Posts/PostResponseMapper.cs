using Microsoft.Extensions.Logging;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Entity + ảnh + thẻ tác giả → <see cref="PostResponse"/>, kèm bước ký presigned GET (Đ-2.9). MỘT chỗ ánh xạ cho D5, D6
/// và D7: hai chỗ là hai chỗ lệch nhau, và cái lệch ở đây có hình dạng "một endpoint trả key thô" hoặc "một endpoint trả
/// <c>reactionCounts: null</c>".
///
/// Là lớp có phụ thuộc (<see cref="IObjectStorage"/>, <see cref="ILogger"/>) chứ không phải hàm tĩnh: ký URL cần khóa R2.
/// <c>CreatePresignedGet</c> là HMAC cục bộ nên ký 20 URL cho một trang feed không tốn lời gọi mạng nào.
/// </summary>
public sealed class PostResponseMapper(IObjectStorage storage, ILogger<PostResponseMapper> logger)
{
    /// <summary>
    /// Tên hiển thị dự phòng khi <c>IUserDirectory</c> không trả về tác giả (<b>Q-D8</b>).
    ///
    /// Đ-2.4 làm ca này gần như không xảy ra — không có hồ sơ thì không đăng được bài, và GĐ2 không có đường xóa hồ sơ —
    /// nhưng "gần như" không phải "không": GĐ8 sẽ có xóa tài khoản, và một bài mồ côi tác giả <b>không được</b> làm 500
    /// cả trang feed của GĐ4. Dự phòng + một dòng cảnh báo, không ném, không thêm nhánh nào khác.
    /// </summary>
    public const string UnknownAuthorName = "Người dùng";

    /// <summary>Một bài. <paramref name="author"/> <c>null</c> = không tra được, xem <see cref="UnknownAuthorName"/>.</summary>
    public PostResponse ToResponse(Post post, IReadOnlyList<MediaAttachment> attachments, UserCard? author, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(attachments);

        if (author is null)
            // CHỈ postId, KHÔNG id người dùng: dòng log này nằm trong hệ thống log chung (Mục 1.3 luật 9).
            logger.LogWarning("Bài {PostId} không tra được tác giả trong IUserDirectory", post.PostId);

        return new PostResponse(
            post.PostId,
            new PostAuthor(
                post.AuthorId,
                author?.DisplayName ?? UnknownAuthorName,
                author?.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null),
            post.Body,
            post.Privacy,
            // Sắp theo Position chứ không tin thứ tự store trả về: vị trí là thứ người dùng nhìn thấy, và một câu
            // truy vấn không ORDER BY thì Postgres được phép trả bất kỳ thứ tự nào.
            [.. attachments
                .OrderBy(m => m.Position)
                .Select(m => new PostMedia(
                    storage.CreatePresignedGet(m.StorageKey),
                    m.ContentType,
                    m.Width,
                    m.Height,
                    m.Position))],
            post.CommentCount,
            // Trả nguyên dictionary của entity — `{}` khi rỗng. Đừng "cho gọn" thành null (Đ-2.12).
            post.ReactionCounts,
            post.CreatedAt,
            post.EditedAt,
            post.AuthorId == actorId);
    }

    /// <summary>
    /// Bản lô cho <c>D6</c> (<c>GET /users/{userId}/posts</c>): một trang bài, ảnh đã gom sẵn theo <c>postId</c>, và
    /// <paramref name="cards"/> là kết quả của ĐÚNG MỘT lời gọi <c>IUserDirectory.GetManyAsync</c> cho cả trang (Đ-2.3).
    /// Gọi <see cref="ToResponse"/> trong vòng lặp với một lời gọi directory mỗi bài là mở lại N+1 đúng ở endpoint trọng
    /// điểm hiệu năng của GĐ4.
    ///
    /// Từ C3 (GĐ4) chỉ <see cref="PostHydrator"/> gọi hàm này — danh sách mới thì gọi hydrator, đừng gọi thẳng đây.
    /// </summary>
    public IReadOnlyList<PostResponse> ToResponses(
        IReadOnlyList<Post> posts,
        IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachment>> attachmentsByPost,
        IReadOnlyDictionary<Guid, UserCard> cards,
        Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(posts);
        ArgumentNullException.ThrowIfNull(attachmentsByPost);
        ArgumentNullException.ThrowIfNull(cards);

        return [.. posts.Select(p => ToResponse(
            p,
            attachmentsByPost.TryGetValue(p.PostId, out var media) ? media : [],
            cards.GetValueOrDefault(p.AuthorId),
            actorId))];
    }
}
