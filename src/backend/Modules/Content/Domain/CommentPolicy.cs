namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// FR-007 (nội dung bình luận, Đ-3.14) dưới dạng HÀM THUẦN — đúng nếp <see cref="PostContentPolicy"/>: đầu vào là giá
/// trị, đầu ra là kết quả, không I/O, không <c>DbContext</c>.
///
/// Khác <see cref="PostContentPolicy"/> ở một điểm: bình luận KHÔNG có ảnh, nên chỉ một mệnh đề — <c>body</c>
/// không rỗng sau khi bỏ khoảng trắng và không vượt <see cref="MaxBodyLength"/>. Cột DB là
/// <c>varchar(1000) NOT NULL</c> (Mục 4 GĐ2).
/// </summary>
public static class CommentPolicy
{
    /// <summary>Độ dài tối đa của <c>body</c> — khớp <c>varchar(1000)</c> của cột.</summary>
    public const int MaxBodyLength = 1000;

    /// <summary>Key lỗi — D3/D4 đặt thẳng vào <c>errors</c> của Problem Details.</summary>
    public const string BodyKey = "body";

    public static readonly string BodyTooLong = $"Bình luận không được vượt quá {MaxBodyLength} ký tự.";
    public const string Empty = "Bình luận không được để trống.";

    /// <summary>Kiểm FR-007 cho một bình luận sắp tạo.</summary>
    /// <param name="body">Nội dung client gửi; <c>null</c> và chuỗi toàn khoảng trắng là như nhau.</param>
    public static CommentValidation Validate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new CommentValidation(BodyKey, Empty);

        if (body.Length > MaxBodyLength)
            return new CommentValidation(BodyKey, BodyTooLong);

        return CommentValidation.Valid;
    }
}

/// <summary>
/// Kết quả của <see cref="CommentPolicy.Validate"/>. Cùng khuôn <see cref="PostContentValidation"/> — không
/// dùng lại nó vì luật bình luận độc lập với BR-01, dù hình dạng giống hệt.
/// </summary>
public readonly record struct CommentValidation(string? ErrorKey, string? Message)
{
    public static readonly CommentValidation Valid = new(null, null);
    public bool IsValid => ErrorKey is null;
}