using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>
/// Khớp <c>RegisterRequest</c> của hợp đồng. Class <c>init</c> thay vì positional record: ở record,
/// <c>[property: Required]</c> được Swagger thấy nhưng MVC bỏ qua, còn <c>[Required]</c> trên tham số thì ngược lại
/// (đã kiểm sau D1). Class chỉ có một chỗ đặt attribute.
///
/// <c>[Required]</c> CHỈ để Swagger ghi <c>required: [email, password]</c> (cổng hợp đồng chiều 2) — nó thuộc
/// DataAnnotations, không phải MVC. Validate thật do <see cref="RegisterRequestValidator"/>; DataAnnotations
/// validation đã tắt ở host, vì để bật thì lỗi của nó dồn chung key PascalCase <c>Email</c> kèm thông điệp tiếng Anh.
///
/// KHÔNG dùng từ khóa C# <c>required</c> (thi công D9): System.Text.Json (.NET 8) chặn body thiếu trường TRƯỚC FluentValidation →
/// người dùng không bao giờ nhận "Email là bắt buộc.", chỉ nhận một lỗi JSON chung. Thiếu trường thì giá trị là chuỗi rỗng và
/// validator bắt.
/// </summary>
public sealed class RegisterRequest
{
    [Required]
    public string Email { get; init; } = "";

    [Required]
    public string Password { get; init; } = "";
}
