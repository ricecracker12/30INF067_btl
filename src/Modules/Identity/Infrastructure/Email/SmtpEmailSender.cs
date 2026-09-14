using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using SocialApp.Modules.Identity.Application.Email;

namespace SocialApp.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Gửi mail xác minh bằng <see cref="SmtpClient"/> của BCL (Đ-D9) — đủ cho Mailpit (1025, không TLS) và SMTP thật
/// (587, STARTTLS qua EnableSsl). KHÔNG log địa chỉ nhận, link hay token.
/// </summary>
internal sealed class SmtpEmailSender(SmtpOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    // Gửi mail nằm TRONG transaction đăng ký (Đ-D5): SMTP treo thì giữ dòng khóa tới hết hạn này rồi rollback.
    // SendMailAsync bỏ qua SmtpClient.Timeout nên phải tự cắt bằng token.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    public async Task SendVerificationAsync(string toEmail, string plainToken, CancellationToken ct)
    {
        var link = $"{options.FrontendBaseUrl}/verify-email?token={plainToken}";

        using var message = new MailMessage(options.From, toEmail)
        {
            Subject = "Xác minh email đăng ký SocialApp",
            SubjectEncoding = Encoding.UTF8,
            Body = $"""
                Chào bạn,

                Bấm vào liên kết dưới đây để xác minh email và kích hoạt tài khoản SocialApp.
                Liên kết có hiệu lực trong 24 giờ và chỉ dùng được một lần.

                {link}

                Nếu bạn không đăng ký tài khoản, hãy bỏ qua email này.
                """,
            BodyEncoding = Encoding.UTF8,
            // Base64 chứ không quoted-printable: QP ngắt dòng ở 76 ký tự và mã hóa "=" trong URL.
            BodyTransferEncoding = TransferEncoding.Base64,
        };

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (options.User is not null)
            client.Credentials = new NetworkCredential(options.User, options.Password);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SendTimeout);
        await client.SendMailAsync(message, timeout.Token);

        logger.LogInformation("Đã gửi mail xác minh qua {SmtpHost}:{SmtpPort}", options.Host, options.Port);
    }
}
