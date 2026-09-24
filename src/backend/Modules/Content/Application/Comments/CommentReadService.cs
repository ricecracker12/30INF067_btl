using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Application.Reactions;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Application.Comments;

/// <summary>
/// D1 + D2 GĐ3 — hai danh sách bình luận tải lười theo cấp (Đ-3.6). Mỗi trang: BR-02 của bài (<see cref="PostAccess"/>) → một
/// câu lấy trang → một lô <see cref="IUserDirectory"/> → một lô <c>myReaction</c>. Số câu không phụ thuộc số bình luận (Mục 7.1).
/// </summary>
public sealed class CommentReadService(
    ICommentStore comments,
    PostAccess access,
    IUserDirectory directory,
    IReactionReader reactions,
    CommentResponseMapper mapper)
{
    /// <summary>
    /// <c>GET /posts/{postId}/comments</c> — bình luận gốc, cũ trước. Bài không xem được → <see cref="ContentErrors.PostNotFound"/>,
    /// cùng phản hồi với <c>GET /posts/{postId}</c> (Đ-3.3).
    /// </summary>
    public async Task<Result<CommentPage>> ListForPostAsync(
        Guid postId, Guid actorId, string? rawCursor, int limit, CancellationToken ct)
    {
        if (await access.ResolveVisibleAsync(postId, actorId, ct) is null)
            return ContentErrors.PostNotFound;

        var rows = await comments.ListRootsAsync(postId, Decode(rawCursor), limit + 1, ct);
        return await PageAsync(rows, limit, actorId, ct);
    }

    /// <summary>
    /// <c>GET /comments/{commentId}/replies</c> — lần từ bình luận về bài rồi hỏi BR-02 (Mục 6.2). Bình luận không có và bài không
    /// xem được trả CÙNG <see cref="ContentErrors.CommentNotFound"/> (<c>READ-CMT-03</c>). Cha đã xóa vẫn mở được nhánh (Đ-3.5).
    /// </summary>
    public async Task<Result<CommentPage>> ListRepliesAsync(
        Guid commentId, Guid actorId, string? rawCursor, int limit, CancellationToken ct)
    {
        var parent = await comments.FindAsync(commentId, ct);
        if (parent is null)
            return ContentErrors.CommentNotFound;

        // MỘT hàm cho mọi endpoint của GĐ3 (Đ-3.3). Không viết lại ba mệnh đề BR-02 ở đây.
        if (await access.ResolveVisibleAsync(parent.PostId, actorId, ct) is null)
            return ContentErrors.CommentNotFound;   // CÙNG phản hồi với "không tồn tại"

        var rows = await comments.ListRepliesAsync(commentId, Decode(rawCursor), limit + 1, ct);
        return await PageAsync(rows, limit, actorId, ct);
    }

    /// <summary>Validator đã kiểm cursor; giải mã lại để service không giả định mình luôn được gọi qua MVC.</summary>
    private static KeysetCursor? Decode(string? raw) => KeysetCursor.TryDecode(raw, out var c) ? c : null;

    private async Task<CommentPage> PageAsync(IReadOnlyList<Comment> rows, int limit, Guid actorId, CancellationToken ct)
    {
        var items = rows.Count > limit ? rows.Take(limit).ToList() : [.. rows];
        if (items.Count == 0)
            return new CommentPage([], null);

        // Neo vào dòng CUỐI của trang trả về, không vào dòng thừa thứ limit+1 (cùng lý do PostReadService.ListByUserAsync).
        var next = rows.Count > limit ? new KeysetCursor(items[^1].CreatedAt, items[^1].CommentId).Encode() : null;

        // Chỉ bình luận còn hiển thị cần tác giả và myReaction — dòng đã xóa không lộ hai thứ đó (mapper cũng chặn lần nữa).
        var visible = items.Where(c => c.Status == CommentStatus.Visible).ToList();
        var cards = visible.Count == 0
            ? new Dictionary<Guid, UserCard>()
            : await directory.GetManyAsync([.. visible.Select(c => c.AuthorId).Distinct()], ct);
        var mine = visible.Count == 0
            ? new Dictionary<Guid, ReactionType>()
            : await reactions.GetMineAsync(actorId, ReactionTargetType.Comment, [.. visible.Select(c => c.CommentId)], ct);

        return new CommentPage(mapper.ToResponses(items, cards, mine, actorId), next);
    }
}
