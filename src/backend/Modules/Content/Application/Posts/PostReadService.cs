using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Nghiệp vụ ĐỌC của bài đăng — tách khỏi <see cref="PostService"/> (đường ghi) vì hai đường không dùng chung một phụ
/// thuộc nào ngoài store và mapper: đọc cần <see cref="IFriendshipReader"/> (BR-02) mà ghi không, ghi cần
/// <c>IObjectStorage</c> (HEAD) mà đọc không.
/// </summary>
public sealed class PostReadService(
    IPostStore posts,
    IUserDirectory directory,
    IFriendshipReader friends,
    PostResponseMapper mapper)
{
    /// <summary>
    /// <c>GET /posts/{postId}</c> — BR-02 tại thời điểm đọc (Mục 7.4).
    ///
    /// <b>Ba lý do trượt, MỘT phản hồi 404</b> (quy ước 3b): bài không tồn tại, bài đã xóa mềm (query filter loại sẵn),
    /// và bài không được xem. Trả 403 cho ca cuối là để status code tự tố cáo bài có tồn tại — chính thứ BR-02 dựng ra
    /// để giấu. Đây là dòng <c>READ-01</c> của AuthZ matrix.
    ///
    /// <c>AreFriendsAsync</c> gọi CÓ ĐIỀU KIỆN — chỉ khi <c>privacy == Friends</c> và người đọc không phải tác giả. Ở
    /// GĐ2 nó là <c>AlwaysStrangers</c> nên miễn phí, nhưng GĐ4 là một lượt đi DB (hoặc cache): gọi vô điều kiện ở đây
    /// là mỗi lần đọc một bài công khai cũng tốn một lượt tra quan hệ bạn bè.
    /// </summary>
    public async Task<Result<PostResponse>> GetAsync(Guid postId, Guid actorId, CancellationToken ct)
    {
        var post = await posts.FindAsync(postId, ct);
        if (post is null)
            return ContentErrors.PostNotFound;

        var areFriends = post.Privacy == PostPrivacy.Friends
            && post.AuthorId != actorId
            && await friends.AreFriendsAsync(actorId, post.AuthorId, ct);

        if (!PostVisibility.CanView(post.Privacy, post.AuthorId, actorId, areFriends))
            return ContentErrors.PostNotFound;

        var media = await posts.MediaOfAsync([post.PostId], ct);
        var cards = await directory.GetManyAsync([post.AuthorId], ct);

        return mapper.ToResponse(
            post,
            media.TryGetValue(post.PostId, out var attachments) ? attachments : [],
            cards.GetValueOrDefault(post.AuthorId),
            actorId);
    }

    /// <summary>
    /// <c>GET /users/{userId}/posts</c> — một trang bài của một người, mới nhất trước (Đ-2.11).
    ///
    /// <b>Không có 404 ở đây.</b> Người dùng không tồn tại, chưa có bài, hay có bài mà người gọi không được xem — cả ba
    /// đều là <c>items: []</c>. Module này không có bảng <c>users</c> để phân biệt (Đ-2.2), và kể cả có thì phân biệt
    /// cũng là biến endpoint thành máy dò "id này có tồn tại không".
    ///
    /// <b>Ba câu truy vấn cho cả trang, không phụ thuộc số bài</b> (Đ-2.3): một câu lấy bài, một câu lấy ảnh của cả
    /// trang, một câu lấy tác giả. Ở endpoint này tác giả luôn là MỘT người, nhưng vẫn gọi bản lô để GĐ4 chép nguyên
    /// hàm này cho feed mà không phải sửa hình dạng.
    /// </summary>
    public async Task<PostPage> ListByUserAsync(
        Guid userId, Guid actorId, string? rawCursor, int limit, CancellationToken ct)
    {
        // Validator đã kiểm; giải mã lại ở đây để service không giả định mình luôn được gọi qua MVC. Cursor hỏng lọt tới
        // đây (chỉ xảy ra khi gọi thẳng service) thì coi như trang đầu — service không có đường trả 400.
        PostCursor? cursor = PostCursor.TryDecode(rawCursor, out var decoded) ? decoded : null;

        // MỘT lần cho cả trang, trước truy vấn. Gọi trong vòng lặp theo từng bài là N lượt ở GĐ4.
        var areFriends = userId != actorId && await friends.AreFriendsAsync(actorId, userId, ct);

        // +1 để biết CÒN TRANG SAU hay không, không phải để trả về.
        var rows = await posts.ListByAuthorAsync(userId, actorId, areFriends, cursor, limit + 1, ct);

        var items = rows.Count > limit ? rows.Take(limit).ToList() : [.. rows];
        if (items.Count == 0)
            return new PostPage([], null);

        var media = await posts.MediaOfAsync([.. items.Select(p => p.PostId)], ct);
        var cards = await directory.GetManyAsync([.. items.Select(p => p.AuthorId).Distinct()], ct);

        // Cursor dựng từ bài CUỐI của trang trả về, KHÔNG phải từ dòng thừa thứ limit+1: dòng thừa không đi vào phản
        // hồi, nên neo vào nó là bỏ qua đúng một bài ở trang sau.
        var next = rows.Count > limit
            ? new PostCursor(items[^1].CreatedAt, items[^1].PostId).Encode()
            : null;   // hết dữ liệu → null, KHÔNG phải "" (Đ-2.11)

        return new PostPage(
            mapper.ToResponses(items, media, cards, actorId),
            next);
    }
}
