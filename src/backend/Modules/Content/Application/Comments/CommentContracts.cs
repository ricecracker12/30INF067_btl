using System.ComponentModel.DataAnnotations;
using FluentValidation;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Comments;

/// <summary>
/// Body của <c>POST /posts/{postId}/comments</c> (Mục 8.1). KHÔNG có <c>depth</c> — server tính cấp từ cha (Đ-3.4) — và không có
/// trường nào mang id người dùng: tác giả luôn là người gọi.
/// </summary>
public sealed class CreateCommentRequest
{
    /// <summary>1–1000 ký tự UTF-16, không toàn khoảng trắng, lưu nguyên văn (Đ-3.14). <c>[Required]</c> chỉ cho Swagger.</summary>
    [Required]
    public string? Body { get; init; }

    /// <summary>Bình luận được trả lời; vắng = bình luận gốc.</summary>
    public Guid? ParentId { get; init; }
}

/// <summary>FR-007 qua HÀM THUẦN của Domain, không gõ lại luật ở đây — cùng khuôn <c>CreatePostRequestValidator</c>.</summary>
public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x).Custom((request, ctx) =>
        {
            var check = CommentPolicy.Validate(request.Body);
            if (!check.IsValid)
                ctx.AddFailure(check.ErrorKey!, check.Message!);
        });
    }
}

/// <summary>
/// Query string của hai danh sách bình luận (<c>cursor</c>, <c>limit</c> — Đ-3.6). Cùng hình với <see cref="ListUserPostsQuery"/>
/// nhưng là kiểu riêng: thông điệp lỗi nói "bình luận", và cursor giải mã bằng <see cref="KeysetCursor"/> chung.
/// </summary>
public sealed class CommentPageQuery
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public string? Cursor { get; init; }

    /// <summary><c>int?</c> để "không gửi" là mặc định 20, không phải 0 → 400 (cùng bẫy của <see cref="ListUserPostsQuery.Limit"/>).</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

public sealed class CommentPageQueryValidator : AbstractValidator<CommentPageQuery>
{
    public static readonly string LimitOutOfRange = $"Số bình luận mỗi trang phải từ 1 đến {CommentPageQuery.MaxLimit}.";

    public CommentPageQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, CommentPageQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || KeysetCursor.TryDecode(raw, out _))
            .WithMessage(ListUserPostsQueryValidator.CursorInvalid);
    }
}

/// <summary>
/// Body của mọi endpoint trả bình luận — khớp schema <c>CommentResponse</c> (Mục 8.1). Bình luận không còn <c>visible</c> vẫn
/// có mặt (giữ nhánh) nhưng <see cref="Author"/>, <see cref="Body"/> là <c>null</c> và không có cảm xúc — xóa ở
/// <see cref="CommentResponseMapper"/>, chỗ DUY NHẤT dựng kiểu này.
/// </summary>
/// <param name="ReplyCount">Phản hồi TRỰC TIẾP, mọi trạng thái — khớp số dòng FE thấy khi mở nhánh (Đ-3.5).</param>
/// <param name="CanDelete">Do server tính: người gọi là tác giả và bình luận còn <c>visible</c>.</param>
public sealed record CommentResponse(
    Guid CommentId,
    Guid PostId,
    Guid? ParentId,
    int Depth,
    CommentStatus Status,
    PostAuthor? Author,
    string? Body,
    int ReplyCount,
    IReadOnlyDictionary<string, int> ReactionCounts,
    ReactionType? MyReaction,
    DateTimeOffset CreatedAt,
    bool CanDelete);

/// <param name="NextCursor"><c>null</c> khi hết dữ liệu — không phải chuỗi rỗng.</param>
public sealed record CommentPage(IReadOnlyList<CommentResponse> Items, string? NextCursor);

/// <summary>Kết quả ghi của <see cref="ICommentStore.AddAsync"/>.</summary>
public enum CommentAddOutcome
{
    Added,

    /// <summary>Bài không còn <c>published</c> lúc khóa (vừa bị xóa/ẩn giữa lúc kiểm BR-02 và lúc ghi) → 404.</summary>
    PostGone,

    /// <summary>Cha không còn <c>visible</c> lúc khóa (vừa bị xóa) → 400 <c>errors.parentId</c>.</summary>
    ParentGone,
}

/// <summary>
/// Bảng <c>content.comments</c> (D1–D4). Hiện thực EF + SQL ở <c>Infrastructure/Persistence</c>. Mọi đường đọc cho NGƯỜI DÙNG phải
/// đi qua <see cref="PostAccess"/> TRƯỚC khi gọi store này — store không biết BR-02 (B.9 mục 3).
/// </summary>
public interface ICommentStore
{
    /// <summary>Một bình luận theo PK, MỌI trạng thái, không tracking — người gọi tự quyết trạng thái nào được đi tiếp.</summary>
    Task<Comment?> FindAsync(Guid commentId, CancellationToken ct);

    /// <summary>Bình luận gốc của bài, keyset <c>(created_at ASC, comment_id ASC)</c> trên <c>idx_comments_post_roots</c>.</summary>
    Task<IReadOnlyList<Comment>> ListRootsAsync(Guid postId, KeysetCursor? cursor, int take, CancellationToken ct);

    /// <summary>Phản hồi trực tiếp của một bình luận, keyset ASC trên <c>idx_comments_parent</c>.</summary>
    Task<IReadOnlyList<Comment>> ListRepliesAsync(Guid parentId, KeysetCursor? cursor, int take, CancellationToken ct);

    /// <summary>
    /// Transaction Mục 7.2 bước 4, khóa theo thứ tự <b>BÀI → CHA</b> (Đ-3.8): <c>comment_count + 1</c>, <c>reply_count + 1</c> của
    /// cha nếu có, rồi INSERT.
    /// </summary>
    Task<CommentAddOutcome> AddAsync(Comment comment, CancellationToken ct);

    /// <summary>
    /// Transaction Mục 7.3, khóa <b>BÀI trước</b>: đổi <c>status</c> sang <c>deleted</c> CHỈ khi dòng còn <c>visible</c> và của
    /// <paramref name="actorId"/>; trừ <c>comment_count</c> chỉ khi đổi được đúng một dòng — hai tab cùng bấm Xóa không trừ hai lần.
    /// </summary>
    /// <returns><c>false</c> khi không đổi được dòng nào — người gọi trả 403.</returns>
    Task<bool> SoftDeleteAsync(Comment comment, Guid actorId, DateTimeOffset now, CancellationToken ct);
}
