using System.Text;
using FluentValidation;
using SocialApp.Modules.Identity.Application.Registration;

namespace SocialApp.Modules.Identity.Application.Login;

/// <summary>
/// Khớp <c>LoginRequest</c> của hợp đồng: <c>email</c> ≤ 254, <c>password</c> ≤ 72. KHÔNG kiểm độ dài tối thiểu hay định
/// dạng email ở đây — email sai định dạng không khớp tài khoản nào nên đi đường 401 chung, và luật mật khẩu đăng ký có
/// thể đổi mà tài khoản cũ vẫn phải đăng nhập được.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email là bắt buộc.")
            .MaximumLength(RegisterRequestValidator.MaxEmailLength).WithMessage("Email tối đa 254 ký tự.");

        RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mật khẩu là bắt buộc.")
            .Must(p => Encoding.UTF8.GetByteCount(p) <= RegisterRequestValidator.MaxPasswordBytes)
            .WithMessage("Mật khẩu tối đa 72 byte.");
    }
}
