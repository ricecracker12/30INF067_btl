namespace SocialApp.Modules.Content.Application;

/// <summary>
/// Sáu mã quyền tầng 2 mà module Content dùng (bốn của GĐ2, hai của GĐ3), dưới dạng hằng chuỗi CỦA CHÍNH MODULE.
///
/// Vì sao không <c>using SocialApp.Modules.Identity.Domain.PermissionCodes</c>: luật 4 của khối D cấm import chéo module
/// (<c>ModuleBoundaryTests</c> canh). Mã quyền là chuỗi trong HỢP ĐỒNG giữa hai module — Content bám vào chuỗi, không bám
/// vào kiểu của Identity.
///
/// Cái giá là gõ sai không bị compiler bắt, và Admin VẪN QUA (short-circuit của <c>PermissionHandler</c> không nhìn mã) nên
/// người test tay bằng Admin thấy "chạy tốt" trong khi USER bị chặn vĩnh viễn. Lưới cho đúng chuyện đó:
/// <c>ContentPermissionsTests</c> ở ArchitectureTests — project test tham chiếu được cả hai module — khẳng định mọi hằng ở
/// đây nằm trong <c>PermissionCodes.All</c>.
/// </summary>
public static class ContentPermissions
{
    /// <summary>Đăng bài (<c>POST /posts</c>), và <c>purpose=post</c> của <c>POST /media/uploads</c> (Q-D5).</summary>
    public const string PostCreate = "post.create";

    /// <summary>Đọc bài công khai (<c>GET /posts/{postId}</c>, <c>GET /users/{userId}/posts</c>).</summary>
    public const string PostReadPublic = "post.read.public";

    /// <summary>Sửa bài của mình (<c>PATCH /posts/{postId}</c>). Ownership là tầng 3, không phải quyền này.</summary>
    public const string PostUpdate = "post.update";

    /// <summary>Xóa bài của mình (<c>DELETE /posts/{postId}</c>). Ownership là tầng 3, không phải quyền này.</summary>
    public const string PostDelete = "post.delete";

    /// <summary>
    /// Bình luận vào bài hoặc trả lời bình luận (<c>POST /posts/{postId}/comments</c>, GĐ3 Đ-3.2). Đọc bình luận dùng
    /// <see cref="PostReadPublic"/>; XÓA bình luận của mình chỉ cần <c>[Authorize]</c> — quyền của chủ dữ liệu, không phải tính
    /// năng được cấp.
    /// </summary>
    public const string CommentCreate = "comment.create";

    /// <summary>Thả / đổi / gỡ cảm xúc trên bài hoặc bình luận (<c>PUT</c>/<c>DELETE …/reactions/me</c>, GĐ3 Đ-3.2).</summary>
    public const string ReactionSet = "reaction.set";

    /// <summary>Mọi mã của module, cho test kiến trúc đọc — thêm hằng mới thì thêm vào đây, nếu không nó không được canh.</summary>
    public static readonly string[] All = [PostCreate, PostReadPublic, PostUpdate, PostDelete, CommentCreate, ReactionSet];
}
