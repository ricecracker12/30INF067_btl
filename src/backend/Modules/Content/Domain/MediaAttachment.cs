using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Một ảnh đã gắn vào bài (ENT-08, bảng <c>content.media_attachments</c>). Bảng ĐA HÌNH theo PTTK:
/// <c>owner_type</c> nhận cả <c>post</c> lẫn <c>message</c> (Đ-2.12).
///
/// Vì đa hình nên KHÔNG có khóa ngoại tới <c>posts</c> — toàn vẹn do service giữ trong một transaction,
/// và đó là lý do THỨ HAI worker dọn rác tồn tại (lý do thứ nhất là object mồ côi trên R2).
///
/// Khối A không chạm R2: <see cref="StorageKey"/> ở đây chỉ là một chuỗi. Ký presign, HEAD lại để kiểm
/// dung lượng/loại thật, và kiểm tiền tố <c>posts/{actorId}/</c> là việc của C3 và D5 (Đ-2.7, Đ-2.8).
/// </summary>
public sealed class MediaAttachment
{
    /// <summary>
    /// Dung lượng tối đa một ảnh: 10 MB (Đ-2.8). DB canh bằng <c>ck_media_size</c>; C3 ký kèm
    /// <c>Content-Length</c> và HEAD lại bằng chính con số này — hai bên lấy chung một hằng số nên không
    /// thể lệch.
    /// </summary>
    public const int MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Ba loại ảnh được nhận (Đ-2.8), đúng thứ tự liệt kê trong <c>ck_media_content_type</c> của Mục 4.
    /// Nguồn duy nhất cho cả CHECK ở DB lẫn allowlist lúc ký presign ở C3.
    /// </summary>
    public static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Khóa chính UUID v7.</summary>
    public Guid MediaId { get; init; } = Uuid7.New();

    /// <summary>Loại chủ sở hữu. DB có CHECK <c>ck_media_owner_type</c> canh lại.</summary>
    public required MediaOwnerType OwnerType { get; init; }

    /// <summary>
    /// <c>post_id</c> (GĐ2) hoặc <c>message_id</c> (GĐ5). KHÔNG FK — xem phần đầu lớp.
    /// </summary>
    public required Guid OwnerId { get; init; }

    /// <summary>
    /// Key của object trên R2, <c>varchar(200)</c>, <b>UNIQUE</b>. UNIQUE chính là thứ chặn "gắn cùng một
    /// object vào hai bài"; hệ quả là commit lại cùng danh sách key sẽ ném <c>DbUpdateException</c> và D5
    /// PHẢI dịch thành 409, không để rơi thành 500 (POST-08).
    /// </summary>
    public required string StorageKey { get; init; }

    /// <summary>MIME type, <c>varchar(40)</c>. DB có CHECK allowlist <c>ck_media_content_type</c> (A5).</summary>
    public required string ContentType { get; init; }

    /// <summary>Dung lượng thật của object (byte), <c>integer</c>. DB có CHECK <c>ck_media_size</c>: &gt; 0 và ≤ 10 MB.</summary>
    public required int SizeBytes { get; init; }

    /// <summary>Chiều rộng ảnh (px), <c>smallint</c>; <c>null</c> khi chưa đọc được kích thước.</summary>
    public short? Width { get; init; }

    /// <summary>Chiều cao ảnh (px), <c>smallint</c>; <c>null</c> khi chưa đọc được kích thước.</summary>
    public short? Height { get; init; }

    /// <summary>
    /// Thứ tự hiển thị trong bài, 0..9 (<c>ck_media_position</c>) — đúng bằng
    /// <see cref="PostContentPolicy.MaxMediaCount"/> vị trí. <c>uq_media_owner_position</c> chặn hai ảnh
    /// cùng một vị trí trong cùng một bài.
    /// </summary>
    public required short Position { get; init; }

    /// <summary>Thời điểm tạo, do đồng hồ app gán. Bảng này KHÔNG có <c>updated_at</c>: ảnh đã gắn thì không sửa.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
