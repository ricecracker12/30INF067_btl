using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Content.Application.Media;

/// <summary>
/// Khai báo của client cho MỘT file, khớp schema <c>UploadFileDeclaration</c> của <c>content-v1.yaml</c>. Hai giá trị này
/// là thứ server ký vào URL (Đ-2.8 lớp 1): <c>Content-Type</c> và <c>Content-Length</c> nằm trong signed headers, nên
/// <c>PUT</c> lệch một byte hay lệch kiểu thì R2 trả 403 — presigned PUT tự nó không giới hạn dung lượng.
///
/// Đây là KHAI BÁO, không phải sự thật: client nói "1 MB, JPEG" thì ta ký đúng chừng đó, còn sự thật chỉ biết được khi
/// <c>HEAD</c> lại lúc commit (Đ-2.8 lớp 2, D3/D5). Endpoint này không chạm R2 lẫn DB nên không có gì để đối chiếu.
///
/// Class <c>init</c> + <c>[Required]</c> chỉ-để-Swagger, KHÔNG dùng từ khóa C# <c>required</c> (Mục 1.3 luật 10): thiếu
/// trường thì validator bắt với thông điệp tiếng Việt, chứ không phải System.Text.Json ném với câu tiếng Anh.
/// </summary>
public sealed class UploadFileDeclaration
{
    /// <summary>MIME type client khai. Allowlist kiểm ở <see cref="CreateUploadsRequestValidator"/>.</summary>
    [Required]
    public string ContentType { get; init; } = "";

    /// <summary>
    /// Dung lượng client khai (byte). <c>long</c> chứ không <c>int</c> vì hợp đồng ghi <c>format: int64</c> — nhận
    /// được số ngoài phạm vi rồi TỪ CHỐI ở validator, thay vì để model binding trả "Giá trị phải là số" cho một
    /// con số hợp lệ về mặt cú pháp.
    /// </summary>
    [Required]
    public long SizeBytes { get; init; }
}
