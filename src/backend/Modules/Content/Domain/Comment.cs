using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Bình luận (ENT-03, bảng <c>content.comments</c>) — <b>KHUNG</b> của Đ-2.12: bảng có ràng buộc đầy đủ
/// (<c>ck_comments_depth</c> cho BR-08, FK trong cùng schema <c>content</c>), nhưng GĐ2 KHÔNG có endpoint,
/// KHÔNG có repository, KHÔNG có service nào chạm tới.
///
/// Giới hạn "chỉ thuộc tính, không hành vi" là CÓ CHỦ ĐÍCH: thêm phương thức ở GĐ2 là viết mã cho một hợp
/// đồng chưa tồn tại. GĐ3 được phép ALTER bảng này — một migration nữa là cái giá đã biết trước.
///
/// Lý do bảng có mặt từ GĐ2: <c>posts.comment_count</c> nằm trên <c>posts</c>, nên <c>PostResponse</c> mang
/// được <c>commentCount</c> ngay từ GĐ2 và <c>schema.d.ts</c> của frontend không phải đổi lần thứ hai.
/// </summary>
public sealed class Comment
{
    /// <summary>Khóa chính UUID v7.</summary>
    public Guid CommentId { get; init; } = Uuid7.New();

    /// <summary>FK → <c>content.posts.post_id</c>, ON DELETE CASCADE. FK trong CÙNG schema nên được giữ (Đ-2.2).</summary>
    public required Guid PostId { get; init; }

    /// <summary>FK tự trỏ → <c>comments.comment_id</c>, ON DELETE CASCADE; <c>null</c> = bình luận gốc.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Tác giả, bằng <c>identity.users.user_id</c> — KHÔNG FK chéo schema (Đ-2.2).</summary>
    public required Guid AuthorId { get; init; }

    /// <summary>Độ sâu 1..3 (<c>ck_comments_depth</c> — BR-08), <c>smallint</c> nên kiểu C# là <c>short</c>.</summary>
    public short Depth { get; set; } = 1;

    /// <summary>Nội dung, <c>varchar(1000)</c> NOT NULL — khác <c>Post.Body</c>, bình luận không có ảnh nên không được rỗng.</summary>
    public required string Body { get; set; }

    /// <summary>Xóa mềm để giữ nhánh trả lời bên dưới. DB có CHECK <c>ck_comments_status</c> canh lại.</summary>
    public CommentStatus Status { get; set; } = CommentStatus.Visible;

    /// <summary>Thời điểm tạo, do đồng hồ app gán.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm sửa gần nhất — do override <c>SaveChanges</c> của <c>ContentDbContext</c> (A5) đóng dấu.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm xóa mềm; <c>null</c> = chưa xóa.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
