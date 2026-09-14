using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SocialApp.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Cấu hình gửi mail xác minh (Đ-D9), đọc từ <c>Smtp:*</c> và <c>Frontend:BaseUrl</c>.
///
/// Development: không đặt thì dùng mặc định trong code — Mailpit của compose dev (<c>localhost:1025</c>) và frontend
/// <c>http://localhost:3000</c>. KHÔNG đọc từ deploy/.env (DevEnvFile): file đó mang giá trị STAGING — đọc vào thì
/// mail ở máy dev trỏ về frontend staging trong khi token nằm ở DB local, và mail thử ở máy dev đi thật qua Brevo bằng
/// tài khoản staging.
///
/// Ngoài Development: thiếu Host/Port/From/BaseUrl thì từ chối khởi động, cùng tinh thần RequireConnectionString.
/// </summary>
internal sealed class SmtpOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public required string From { get; init; }
    public required bool EnableSsl { get; init; }

    /// <summary>Gốc URL frontend, không có "/" cuối. Link trong mail: <c>{FrontendBaseUrl}/verify-email?token=…</c>.</summary>
    public required string FrontendBaseUrl { get; init; }

    public static SmtpOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var isDevelopment = environment.IsDevelopment();
        var problems = new List<(string Key, string Text)>();

        string? Read(string key, string developmentDefault)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
            if (isDevelopment)
                return developmentDefault;

            problems.Add((key, $"thiếu {key}"));
            return null;
        }

        var host = Read("Smtp:Host", "localhost");
        var portText = Read("Smtp:Port", "1025");
        var from = Read("Smtp:From", "no-reply@socialapp.local");
        var baseUrl = Read("Frontend:BaseUrl", "http://localhost:3000");

        var port = 0;
        if (portText is not null && (!int.TryParse(portText, out port) || port is <= 0 or > 65535))
            problems.Add(("Smtp:Port", "Smtp:Port không phải số cổng hợp lệ"));
        if (from is not null && !MailAddress.TryCreate(from, out _))
            problems.Add(("Smtp:From", "Smtp:From không phải địa chỉ email"));
        if (baseUrl is not null
            && !(Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            problems.Add(("Frontend:BaseUrl", "Frontend:BaseUrl phải là URL tuyệt đối http(s)"));

        if (problems.Count > 0)
        {
            var variables = string.Join(", ", problems.Select(p => p.Key.Replace(":", "__")).Distinct());
            throw new InvalidOperationException(
                $"Cấu hình gửi mail không hợp lệ ở môi trường '{environment.EnvironmentName}': "
              + $"{string.Join("; ", problems.Select(p => p.Text))}. "
              + $"Đặt biến môi trường {variables} trong deploy/.env rồi deploy lại. "
              + "App từ chối khởi động thay vì chạy tiếp với cấu hình thiếu.");
        }

        var user = configuration["Smtp:User"];
        return new SmtpOptions
        {
            Host = host!,
            Port = port,
            User = string.IsNullOrWhiteSpace(user) ? null : user,
            Password = configuration["Smtp:Password"],
            From = from!,
            // Mailpit (1025) không TLS; SMTP thật (587) có tài khoản → STARTTLS. Có User mà không ghi rõ thì mặc định bật:
            // không bao giờ gửi mật khẩu SMTP qua kết nối thường.
            EnableSsl = configuration.GetValue<bool?>("Smtp:EnableSsl") ?? !string.IsNullOrWhiteSpace(user),
            FrontendBaseUrl = baseUrl!.TrimEnd('/'),
        };
    }
}
