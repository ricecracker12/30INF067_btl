using Microsoft.Extensions.Logging;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Application.Comments;

/// <summary>
/// D3 + D4 GĐ3 — đường GHI của bình luận. Bộ đếm (<c>comment_count</c>, <c>reply_count</c>) đổi trong transaction của
/// <see cref="ICommentStore"/> theo khuôn Đ-3.8, không ở đây; event phát SAU khi store đã commit (Đ-3.12).
/// </summary>
public sealed class CommentService(
    ICommentStore comments,
    PostAccess access,
    IUserDirectory directory,
    CommentResponseMapper mapper,
    ContentInteractionEvents events,
    TimeProvider clock,
    ILogger<CommentService> logger)
{
    /// <summary>
    /// <c>POST /posts/{postId}/comments</c>. <b>Thứ tự kiểm là một phần của hợp đồng</b> (Mục 6.1, cùng nếp
    /// <see cref="PostService.CreateAsync"/>): tầng 2 <c>comment.create</c> (controller) → có hồ sơ (403) → BR-02 của bài (404) →
    /// cha hợp lệ (400 <c>errors.parentId</c>) → transaction.
    /// </summary>
    public async Task<Result<CommentResponse>> CreateAsync(
        Guid postId, Guid actorId, CreateCommentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Đ-2.4: không có hồ sơ thì không có tên để hiện dưới bình luận. UserCard lấy ở đây dùng lại cho response.
        var cards = await directory.GetManyAsync([actorId], ct);
        if (!cards.TryGetValue(actorId, out var author))
            return Result<CommentResponse>.Forbidden();

        // Validator đã chặn; kiểm lại bằng hàm thuần để service không giả định mình luôn được gọi qua MVC.
        var bodyCheck = CommentPolicy.Validate(request.Body);
        if (!bodyCheck.IsValid)
            return ContentErrors.FromValidation(bodyCheck);

        // (2) BR-02 của bài (Đ-3.3) — 404 cho mọi lý do trượt.
        var post = await access.ResolveVisibleAsync(postId, actorId, ct);
        if (post is null)
            return ContentErrors.PostNotFound;

        // (3) Cha (Đ-3.4). Không tồn tại · bài khác · không còn visible → MỘT câu; cấp 3 → câu BR-08.
        Comment? parent = null;
        Comment comment;
        if (request.ParentId is { } parentId)
        {
            parent = await comments.FindAsync(parentId, ct);
            if (parent is null || parent.PostId != postId || parent.Status != CommentStatus.Visible)
                return ContentErrors.ParentGone;

            var depth = CommentDepthPolicy.ValidateReply(parent.Depth);
            if (!depth.IsValid)
                return ContentErrors.FromValidation(depth);

            comment = Comment.CreateReply(parent, actorId, request.Body!);
        }
        else
        {
            comment = Comment.CreateRoot(postId, actorId, request.Body!);
        }

        // (4) Transaction Mục 7.2: khóa bài → cha, hai bộ đếm tăng nguyên tử, INSERT.
        var outcome = await comments.AddAsync(comment, ct);
        switch (outcome)
        {
            case CommentAddOutcome.PostGone:
                return ContentErrors.PostNotFound;
            case CommentAddOutcome.ParentGone:
                return ContentErrors.ParentGone;
        }

        // (5) SAU COMMIT (Đ-3.12). Không log nội dung bình luận (B.9 mục 5).
        events.CommentCreated(comment, post.AuthorId, parent);
        logger.LogInformation("Đã tạo bình luận {CommentId} cấp {Depth} trong bài {PostId}", comment.CommentId, comment.Depth, postId);

        return mapper.ToResponse(comment, author, myReaction: null, actorId);
    }

    /// <summary>
    /// <c>DELETE /comments/{commentId}</c> — xóa MỀM, giữ nhánh (Đ-3.5). Tầng 3 là SỞ HỮU, không phải BR-02: tác giả xóa được
    /// bình luận của mình kể cả khi bài giờ đã private với họ. Không tồn tại · của người khác · đã xóa → <b>403</b>, cùng phản
    /// hồi (<c>TC-A03-comment</c>). KHÔNG có nhánh Admin ở tầng 3.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid commentId, Guid actorId, CancellationToken ct)
    {
        var comment = await comments.FindAsync(commentId, ct);
        if (comment is null || comment.AuthorId != actorId || comment.Status != CommentStatus.Visible)
            return Result.Forbidden();

        // Store kiểm lại tác giả + visible TRONG câu UPDATE: hai tab cùng bấm Xóa thì tab sau nhận false → 403, không trừ hai lần.
        if (!await comments.SoftDeleteAsync(comment, actorId, clock.GetUtcNow(), ct))
            return Result.Forbidden();

        logger.LogInformation("Đã xóa mềm bình luận {CommentId}", commentId);
        return Result.Success();
    }
}
