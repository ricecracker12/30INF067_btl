using FluentValidation;
using SocialApp.Modules.Identity.Application.Security;

namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>
/// Sai định dạng thì 400 ngay, không chạm DB. Đúng định dạng là đúng thứ <see cref="SecureToken.Generate"/> sinh ra:
/// 64 ký tự hex THƯỜNG. Hợp đồng chỉ ghi <c>minLength: 32</c> — chặt hơn không phá hợp đồng, vì token hợp lệ nào cũng 64 hex.
/// </summary>
public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public const int TokenLength = SecureToken.ByteLength * 2;

    public VerifyEmailRequestValidator()
    {
        // So từng ký tự thay vì Matches("^[0-9a-f]{64}$"): "$" của .NET khớp cả trước "\n" cuối chuỗi.
        RuleFor(x => x.Token).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Liên kết xác minh không hợp lệ.")
            .Must(t => t.Length == TokenLength && t.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            .WithMessage("Liên kết xác minh không hợp lệ.");
    }
}
