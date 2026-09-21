using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Một ảnh đã <c>PUT</c> xong, kèm lại khai báo lúc presign — khớp schema <c>MediaKeyDeclaration</c> của
/// <c>content-v1.yaml</c>. Hình dạng này là **mảng object**, không phải mảng chuỗi, và đó là điểm Q-D1 đã chốt ở cổng mở:
/// Đ-2.8 lớp 2 đối chiếu <c>HEAD</c> với "khai báo lúc presign", mà server KHÔNG giữ trạng thái giữa presign và commit.
/// Client (đang cầm <c>File</c>) gửi lại khai báo là đường rẻ nhất, và không có gì để giả — <c>HEAD</c> vẫn là thứ quyết
/// định (<see cref="Domain.MediaHeadPolicy"/>).
///
/// Khác <c>UploadFileDeclaration</c> của D4 đúng một trường: ở đó client chưa có key (server sắp cấp), ở đây client trả
/// lại chính key đã được cấp.
/// </summary>
public sealed class MediaKeyDeclaration
{
    /// <summary>Key do <c>POST /media/uploads</c> cấp. Dạng kiểm ở validator, tiền tố người gọi kiểm ở service (Đ-2.7).</summary>
    [Required]
    public string MediaKey { get; init; } = "";

    /// <summary>Loại ảnh client khai lúc presign — <c>HEAD</c> phải trả lại đúng loại này.</summary>
    [Required]
    public string ContentType { get; init; } = "";

    /// <summary>
    /// Dung lượng client khai lúc presign, byte. <c>long</c> vì hợp đồng ghi <c>format: int64</c>; xuống tới
    /// <see cref="Domain.MediaAttachment.SizeBytes"/> (<c>int</c>) thì ép kiểu, và việc ép đó CHỈ an toàn nhờ validator
    /// đã chặn <c>≤ 10 MB</c> — bỏ ràng buộc đó rồi tin <c>(int)</c> là tràn số âm đi thẳng vào DB.
    /// </summary>
    [Required]
    public long SizeBytes { get; init; }
}
