namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// BR-08 dưới dạng HÀM THUẦN: tối đa 3 cấp bình luận (gốc = 1, trả lời tối đa tới cấp 3).
///
/// Domain KHÔNG tự đi lấy Depth của cha từ DB — service (D3) đọc entity cha rồi truyền <c>parentDepth</c>
/// vào đây; nhờ vậy hàm vẫn thuần và test được bằng số nguyên thường, không cần dựng entity hay DbContext giả.
///
/// CHECK <c>ck_comments_root_depth</c> (Mục 4 GĐ3) canh lại mệnh đề "parent_id IS NULL ⇔ depth = 1" ở DB —
/// lớp trong (đây) cho đúng key lỗi + thông điệp, lớp ngoài chặn cả đường SQL thô, giống cặp
/// BR-01/<c>ck_posts_not_empty</c> của GĐ2.
/// </summary>
public static class CommentDepthPolicy
{
    /// <summary>Độ sâu của bình luận gốc (không có cha).</summary>
    public const short RootDepth = 1;

    /// <summary>Độ sâu tối đa cho phép — BR-08.</summary>
    public const short MaxDepth = 3;

    /// <summary>Key lỗi — gắn vào trường <c>parentId</c> của request, không phải <c>body</c>.</summary>
    public const string ParentIdKey = "parentId";

    public static readonly string TooDeep = $"Chỉ được trả lời tối đa {MaxDepth} cấp.";

    /// <summary>
    /// Cha không tồn tại, thuộc bài khác, hay không còn hiển thị — MỘT câu cho cả ba (Đ-3.4): câu riêng cho "thuộc bài khác" là
    /// kênh dò <c>commentId</c> của bài người khác.
    /// </summary>
    public const string ParentGone = "Bình luận cần trả lời không còn tồn tại.";

    /// <summary>Tính độ sâu cho một phản hồi, dựa trên độ sâu của bình luận cha.</summary>
    /// <param name="parentDepth">Độ sâu của bình luận cha (1 hoặc 2) — D3 đọc từ entity cha trước khi gọi.</param>
    /// <returns>
    /// Hợp lệ với <c>Depth = parentDepth + 1</c>, hoặc lỗi <see cref="ParentIdKey"/> nếu cha đã ở
    /// <see cref="MaxDepth"/> (không cho trả lời tiếp).
    /// </returns>
    public static CommentDepthValidation ValidateReply(short parentDepth)
    {
        if (parentDepth >= MaxDepth)
            return new CommentDepthValidation(ParentIdKey, TooDeep, null);

        return new CommentDepthValidation(null, null, (short)(parentDepth + 1));
    }
}

/// <summary>
/// Kết quả của <see cref="CommentDepthPolicy.ValidateReply"/>. Khác <see cref="PostContentValidation"/> ở
/// một điểm: khi hợp lệ còn mang theo giá trị tính được (<see cref="Depth"/>) — <c>Comment.CreateReply</c>
/// (A2) cần dùng ngay con số này.
/// </summary>
public readonly record struct CommentDepthValidation(string? ErrorKey, string? Message, short? Depth)
{
    public bool IsValid => ErrorKey is null;
}