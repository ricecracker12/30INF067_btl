using System.Net.Mail;
using System.Text;
using FluentValidation;

namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>Khớp ràng buộc của hợp đồng: <c>email</c> ≤ 254, <c>password</c> 8–72.</summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public const int MaxEmailLength = 254;
    public const int MinPasswordLength = 8;

    /// <summary>
    /// Trần CỨNG của BCrypt: byte thứ 73 trở đi bị bỏ qua âm thầm. Đếm BYTE UTF-8, không đếm ký tự — mật khẩu tiếng
    /// Việt 40 ký tự có thể quá 72 byte.
    /// </summary>
    public const int MaxPasswordBytes = 72;

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email là bắt buộc.")
            .MaximumLength(MaxEmailLength).WithMessage("Email tối đa 254 ký tự.")
            .Must(BeSingleMailAddress).WithMessage("Email không đúng định dạng.");

        RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mật khẩu là bắt buộc.")
            .MinimumLength(MinPasswordLength).WithMessage("Mật khẩu phải có ít nhất 8 ký tự.")
            .Must(p => Encoding.UTF8.GetByteCount(p) <= MaxPasswordBytes).WithMessage("Mật khẩu tối đa 72 byte.");
    }

    // MailAddress thay cho EmailAddress() của FluentValidation (chỉ kiểm có '@'): địa chỉ lọt validator mà MailMessage
    // không nhận thì SMTP ném giữa transaction → 500 thay vì 400. Chặn cả dạng "Tên <a@b.com>".
    private static bool BeSingleMailAddress(string email) =>
        MailAddress.TryCreate(email.Trim(), out var address)
        && address.DisplayName.Length == 0
        && string.Equals(address.Address, email.Trim(), StringComparison.Ordinal);
}
