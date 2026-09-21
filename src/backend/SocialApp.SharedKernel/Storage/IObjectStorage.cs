namespace SocialApp.SharedKernel.Storage;

/// <summary>
/// Lưu trữ đối tượng (Cloudflare R2, tương thích S3) — bề mặt ĐÚNG NĂM thao tác, không hơn (Đ-2.14).
///
/// Đặt ở SharedKernel theo tiền lệ <c>SharedKernel/Redis/</c>: Profile (avatar), Content (ảnh bài) và GĐ5
/// (media tin nhắn) dùng chung; không module nào gọi AWS SDK trực tiếp. Giữ hẹp để nếu có ngày bỏ SDK thì đó
/// là thay một class, không phải viết lại luồng.
///
/// Không kiểu nào của AWS SDK lộ ra đây: <see cref="ObjectHead"/>, <see cref="ObjectPage"/>, <see cref="ObjectItem"/>
/// là record của ta. Trả kiểu SDK ra ngoài nghĩa là module Content phải <c>using Amazon.S3.Model</c> — lúc đó
/// "bỏ SDK" không còn là thay một class, và FakeObjectStorage (C5) phải dựng được kiểu của SDK trong test.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// URL đã ký để trình duyệt <c>PUT</c> thẳng lên bucket (Đ-2.5), hạn <see cref="R2Options.PutUrlMinutes"/>.
    /// <paramref name="contentType"/> VÀ <paramref name="contentLength"/> phải nằm trong signed headers (Đ-2.8 lớp 1):
    /// presigned PUT không tự giới hạn dung lượng — ký cho 2MB rồi client PUT 400MB vẫn vào bucket nếu Content-Length
    /// không được ký. Ký là HMAC cục bộ, không gọi mạng.
    /// </summary>
    string CreatePresignedPut(string key, string contentType, long contentLength);

    /// <summary>
    /// URL đã ký để đọc, hạn <see cref="R2Options.GetUrlMinutes"/> (Đ-2.9): ảnh riêng tư không phục vụ bằng bucket
    /// công khai. Ký là HMAC cục bộ — một trang feed ký 20 URL không tốn lời gọi mạng nào.
    /// </summary>
    string CreatePresignedGet(string key);

    /// <summary>
    /// Đọc size + content type THẬT của object trong bucket (Đ-2.8 lớp 2 — lớp duy nhất nói được sự thật).
    /// Trả <c>null</c> khi object không tồn tại; mọi lỗi khác (mạng, khóa sai) ném ra — đó là 500, không phải
    /// "ảnh của bạn không hợp lệ".
    /// </summary>
    Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default);

    /// <summary>Xóa một object. Idempotent: object không tồn tại không phải lỗi.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Liệt kê theo tiền tố, phân trang bằng continuation token — worker dọn rác (C4, Đ-2.13) cần để không giữ
    /// khóa suốt cả tiếng trên bucket lớn.
    /// </summary>
    Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default);
}

/// <summary>Kết quả HEAD: đúng hai thứ Đ-2.8 cần đối chiếu, cộng mốc thời gian cho worker dọn rác.</summary>
public sealed record ObjectHead(long ContentLength, string ContentType, DateTimeOffset LastModified);

/// <summary>Một object trong kết quả liệt kê.</summary>
public sealed record ObjectItem(string Key, long Size, DateTimeOffset LastModified);

/// <summary>Một trang liệt kê; <see cref="NextContinuationToken"/> null = hết.</summary>
public sealed record ObjectPage(IReadOnlyList<ObjectItem> Items, string? NextContinuationToken);
