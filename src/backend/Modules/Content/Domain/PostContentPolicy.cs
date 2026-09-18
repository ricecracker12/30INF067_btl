namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// BR-01 dưới dạng HÀM THUẦN — đúng nếp <c>LockoutPolicy</c>/<c>RefreshTokenPolicy</c> của GĐ1: đầu vào là
/// giá trị, đầu ra là kết quả, không I/O, không <c>DbContext</c>. Nhờ vậy AC-02, AC-03 và BR01-06 kiểm được
/// bằng unit test, không cần Postgres.
///
/// BR-01 gồm ba mệnh đề: <c>body</c> ≤ <see cref="MaxBodyLength"/> · số ảnh ≤ <see cref="MaxMediaCount"/> ·
/// (có ảnh <b>hoặc</b> <c>body</c> không rỗng sau khi bỏ khoảng trắng). DB canh lại mệnh đề thứ ba bằng
/// <c>ck_posts_not_empty</c> và mệnh đề thứ hai bằng <c>ck_posts_media_count</c>; hai lớp này cố ý trùng nhau
/// — lớp trong cho thông điệp lỗi đúng key, lớp ngoài chặn cả đường SQL thô.
///
/// Hai thứ KHÔNG thuộc về đây, đừng thêm vào:
/// <list type="bullet">
/// <item>Kiểm tiền tố <c>posts/{actorId}/</c> của <c>storage_key</c> — cần <c>actorId</c>, là việc của D5 (Đ-2.7).</item>
/// <item>Kiểm dung lượng và loại ảnh THẬT — cần một lệnh <c>HEAD</c> lên R2, là việc của C3 (Đ-2.8).</item>
/// </list>
/// Thêm vào đây thì hàm hết thuần, và cái giá là unit test của BR-01 phải dựng hạ tầng giả.
/// </summary>
public static class PostContentPolicy
{
    /// <summary>Độ dài tối đa của <c>body</c> — bằng đúng <c>varchar(5000)</c> của cột (Mục 4).</summary>
    public const int MaxBodyLength = 5000;

    /// <summary>Số ảnh tối đa một bài, khớp <c>ck_posts_media_count</c> (<c>BETWEEN 0 AND 10</c>).</summary>
    public const int MaxMediaCount = 10;

    /// <summary>Key lỗi cho mệnh đề về chữ — D5 đặt thẳng vào <c>errors</c> của Problem Details (AC-02).</summary>
    public const string BodyKey = "body";

    /// <summary>Key lỗi cho mệnh đề về ảnh. Tên theo trường của request, không theo tên cột DB (AC-03).</summary>
    public const string MediaKeysKey = "mediaKeys";

    // `static readonly` chứ không `const`: C# chỉ cho nội suy hằng khi MỌI phần đều là hằng CHUỖI, nên
    // chèn một hằng số nguyên vào là CS0133. Viết tay "5000" vào câu thì con số trong thông điệp và con số
    // trong luật tách làm hai nguồn — đúng thứ vừa tránh ở trên.
    public static readonly string BodyTooLong = $"Nội dung bài không được vượt quá {MaxBodyLength} ký tự.";
    public static readonly string TooManyMedia = $"Một bài chỉ được đính kèm tối đa {MaxMediaCount} ảnh.";
    public const string Empty = "Bài đăng phải có nội dung hoặc ít nhất một ảnh.";

    /// <summary>
    /// Kiểm BR-01 cho một bài sắp tạo hoặc sắp sửa.
    /// </summary>
    /// <param name="body">Nội dung chữ client gửi; <c>null</c> và chuỗi toàn khoảng trắng là như nhau.</param>
    /// <param name="mediaCount">Số ảnh đính kèm — số phần tử của <c>mediaKeys</c>, KHÔNG phải số ảnh đã có trên R2.</param>
    /// <returns>
    /// <see cref="PostContentValidation.Valid"/>, hoặc kết quả mang đúng MỘT key lỗi. Trả một key chứ không
    /// gom nhiều: <c>errors</c> của RFC 7807 hiện dưới trường nào thì người dùng sửa trường đó, và bài vừa
    /// quá dài vừa quá nhiều ảnh là chuyện hiếm hơn nhiều so với cái giá của hai thông điệp cùng lúc.
    /// </returns>
    public static PostContentValidation Validate(string? body, int mediaCount)
    {
        // Ảnh trước: AC-03 gửi 11 ảnh KÈM body hợp lệ, và nếu kiểm body trước thì kết quả vẫn đúng — nhưng
        // gửi 11 ảnh không kèm chữ (cũng hợp lệ với mệnh đề 3) phải ra "mediaKeys", không phải "body".
        if (mediaCount is < 0 or > MaxMediaCount)
            return new PostContentValidation(MediaKeysKey, TooManyMedia);

        if (body is { Length: > MaxBodyLength })
            return new PostContentValidation(BodyKey, BodyTooLong);

        // Mệnh đề 3. Key là "body" chứ không phải "mediaKeys" (AC-02): người dùng đang ở ô soạn chữ, và
        // bảo họ "thiếu ảnh" trong khi đường thoát dễ nhất là gõ một chữ là chỉ sai chỗ.
        if (mediaCount == 0 && string.IsNullOrWhiteSpace(body))
            return new PostContentValidation(BodyKey, Empty);

        return PostContentValidation.Valid;
    }
}

/// <summary>
/// Kết quả của <see cref="PostContentPolicy.Validate"/>. <c>readonly record struct</c> cùng nếp
/// <c>Result</c>/<c>Error</c> của SharedKernel, nhưng KHÔNG dùng lại <c>Error</c>: <c>Error</c> mang
/// <c>Status</c> HTTP, mà quyết định "BR-01 hỏng thì trả mã nào" là của tầng D, không của Domain.
/// </summary>
/// <param name="ErrorKey">Key trong <c>errors</c> của Problem Details; <c>null</c> khi hợp lệ.</param>
/// <param name="Message">Thông điệp tiếng Việt cho người dùng cuối; <c>null</c> khi hợp lệ.</param>
public readonly record struct PostContentValidation(string? ErrorKey, string? Message)
{
    public static readonly PostContentValidation Valid = new(null, null);

    public bool IsValid => ErrorKey is null;
}
