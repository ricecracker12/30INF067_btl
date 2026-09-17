namespace SocialApp.Modules.Identity.Application.Email;

/// <summary>
/// Gửi mail xác minh (D1). Hiện thực SMTP ở Infrastructure/Email (D1). Khai từ D0 vì harness test auth thay nó
/// bằng <c>CapturingEmailSender</c> để lấy token bản rõ mà không cần Mailpit.
/// </summary>
public interface IEmailSender
{
    /// <summary>Không log <paramref name="plainToken"/> hay link chứa nó (NFR-SEC-01).</summary>
    Task SendVerificationAsync(string toEmail, string plainToken, CancellationToken ct);
}
