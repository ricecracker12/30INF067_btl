using System.Text.Json.Serialization;

namespace SocialApp.Modules.Content.Application.Media;

/// <summary>
/// Một URL đã ký cho một file, khớp schema <c>UploadTicket</c> của <c>content-v1.yaml</c>. Trả về theo ĐÚNG thứ tự của
/// <c>files</c> gửi lên — FE đang cầm mảng <c>File</c> và ghép theo chỉ số, không có gì khác để ghép.
/// </summary>
/// <param name="MediaKey">Key trên R2 (Đ-2.7). Thứ FE gửi lại ở <c>POST /posts</c> hoặc <c>PUT /users/me/avatar</c>.</param>
/// <param name="UploadUrl">
/// Presigned <c>PUT</c>. Mang chữ ký — <b>không bao giờ log</b> (Mục 1.3 luật 9), không lưu, không đưa vào thông điệp lỗi.
/// </param>
/// <param name="ExpiresIn">Số giây còn hạn, luôn <c>600</c> — xem <see cref="UploadTicketService"/>.</param>
/// <param name="RequiredHeaders">Header phải gửi kèm <c>PUT</c> vì chúng nằm trong chữ ký (Đ-2.8 lớp 1).</param>
public sealed record UploadTicket(string MediaKey, string UploadUrl, int ExpiresIn, RequiredHeaders RequiredHeaders);

/// <summary>
/// Hai header trong signed headers của <see cref="UploadTicket.UploadUrl"/>. Là một <b>record có hai trường</b> chứ không
/// phải <c>Dictionary&lt;string, string&gt;</c>: key có gạch nối, mà chính sách camelCase toàn cục của
/// <c>Program.cs</c> sẽ biến <c>Content-Type</c> thành <c>contentType</c> nếu để dictionary — FE gửi sai header thì R2
/// trả 403 và triệu chứng không nói gì về nguyên nhân.
/// </summary>
/// <param name="ContentType">Đúng chuỗi client khai, đã qua allowlist.</param>
/// <param name="ContentLength">
/// <b>Chuỗi, không phải số</b> — header HTTP là chuỗi, và hợp đồng ghi <c>type: string</c>. Trình duyệt tự đặt header này
/// từ body; giá trị ở đây để FE đối chiếu kích thước file TRƯỚC khi gửi.
/// </param>
public sealed record RequiredHeaders(
    [property: JsonPropertyName("Content-Type")] string ContentType,
    [property: JsonPropertyName("Content-Length")] string ContentLength);
