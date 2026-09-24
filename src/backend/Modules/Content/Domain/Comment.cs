using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Bình luận (ENT-03, bảng <c>content.comments</c>). GĐ3 hiện thực đầy đủ: transaction Đ-3.8 (C2/C3), hai
/// endpoint tạo/xóa (D3/D4), 8 endpoint tổng cộng của khối D.
///
/// FK giữ nguyên trong cùng schema <c>content</c> (Đ-2.2). <c>Depth</c> KHÔNG có setter công khai — chỉ được
/// gán qua <see cref="CreateRoot"/>/<see cref="CreateReply"/>, tính bằng <see cref="CommentDepthPolicy"/>:
/// BR-08 chỉ có MỘT nơi quyết định độ sâu, không lặp lại ở service.
/// </summary>
public sealed class Comment
{
    /// <summary>Khóa chính UUID v7.</summary>
    public Guid CommentId { get; init; } = Uuid7.New();

    /// <summary>FK → <c>content.posts.post_id</c>, ON DELETE CASCADE.</summary>
    public required Guid PostId { get; init; }

    /// <summary>FK tự trỏ → <c>comments.comment_id</c>, ON DELETE CASCADE; <c>null</c> = bình luận gốc.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Tác giả, bằng <c>identity.users.user_id</c> — KHÔNG FK chéo schema (Đ-2.2).</summary>
    public required Guid AuthorId { get; init; }

    /// <summary>
    /// Độ sâu 1..3 (<c>ck_comments_depth</c>, <c>ck_comments_root_depth</c> — BR-08). Setter <c>private</c>
    /// có chủ đích: giá trị này CHỈ do <see cref="CommentDepthPolicy"/> tính ra qua hai factory dưới, không
    /// ai được gán tay từ tầng ngoài (kể cả service).
    /// </summary>
    public short Depth { get; private set; } = 1;

    /// <summary>Nội dung, <c>varchar(1000)</c> NOT NULL. D3/D4 kiểm bằng <see cref="CommentPolicy"/> TRƯỚC khi gọi factory.</summary>
    public required string Body { get; set; }

    /// <summary>Xóa mềm để giữ nhánh trả lời bên dưới. DB có CHECK <c>ck_comments_status</c> canh lại.</summary>
    public CommentStatus Status { get; set; } = CommentStatus.Visible;

    /// <summary>
    /// Số phản hồi trực tiếp, mọi trạng thái (kể cả đã xóa — Đ-3.9). C3 ghi bằng SQL trong transaction Đ-3.8,
    /// KHÔNG qua <c>SaveChanges</c> thường — property này tồn tại để D1/D2 đọc và map ra <c>CommentResponse</c>.
    /// </summary>
    public int ReplyCount { get; set; }

    /// <summary>
    /// Đếm cảm xúc theo loại, cột <c>jsonb DEFAULT '{}'::jsonb</c>. Cùng lý do <see cref="Post.ReactionCounts"/>:
    /// C3 ghi bằng SQL, entity chỉ cần map đúng để đọc — <c>ValueComparer</c> BẮT BUỘC khai ở A3/Configuration,
    /// thiếu nó EF không phát hiện thay đổi bên trong dictionary (lỗi câm).
    /// </summary>
    public Dictionary<string, int> ReactionCounts { get; set; } = new();

    /// <summary>Thời điểm tạo, do đồng hồ app gán.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm sửa gần nhất.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm xóa mềm; <c>null</c> = chưa xóa.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Tạo bình luận gốc (<c>parentId == null</c>, <c>depth = 1</c>). D3 gọi SAU khi đã kiểm
    /// <see cref="CommentPolicy.Validate"/> cho <paramref name="body"/> — factory không tự validate lại nội
    /// dung, chỉ đảm bảo bất biến về cấu trúc (Depth/ParentId khớp nhau).
    /// </summary>
    public static Comment CreateRoot(Guid postId, Guid authorId, string body) =>
        new()
        {
            PostId = postId,
            AuthorId = authorId,
            Body = body,
            Depth = CommentDepthPolicy.RootDepth,
        };

    /// <summary>
    /// Tạo một phản hồi của <paramref name="parent"/>. Độ sâu tính từ <c>parent.Depth</c> qua
    /// <see cref="CommentDepthPolicy.ValidateReply"/> — KHÔNG nhận <c>depth</c> làm tham số, để không ai
    /// truyền tay một con số sai.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Cha đã ở <see cref="CommentDepthPolicy.MaxDepth"/>. D3 PHẢI tự gọi
    /// <see cref="CommentDepthPolicy.ValidateReply"/> TRƯỚC (để trả 400 <c>parentId</c> đúng hợp đồng) —
    /// exception ở đây chỉ nổ khi có lỗi lập trình gọi sai thứ tự, không phải đường đi bình thường.
    /// </exception>
    public static Comment CreateReply(Comment parent, Guid authorId, string body)
    {
        var validation = CommentDepthPolicy.ValidateReply(parent.Depth);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Message);

        return new()
        {
            PostId = parent.PostId,
            ParentId = parent.CommentId,
            AuthorId = authorId,
            Body = body,
            Depth = validation.Depth!.Value,
        };
    }
}