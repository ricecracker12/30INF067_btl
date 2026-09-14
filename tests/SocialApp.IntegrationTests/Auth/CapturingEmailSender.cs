using System.Collections.Concurrent;
using SocialApp.Modules.Identity.Application.Email;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// Thay SMTP trong test endpoint: giữ lại token bản rõ để test đi tiếp tới verify-email mà không cần Mailpit.
/// Singleton cho cả factory — test tra mail theo email ngẫu nhiên của mình, không đếm tổng.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<CapturedEmail> _sent = new();

    public IReadOnlyList<CapturedEmail> Sent => [.. _sent];

    public Task SendVerificationAsync(string toEmail, string plainToken, CancellationToken ct)
    {
        _sent.Enqueue(new CapturedEmail(toEmail, plainToken));
        return Task.CompletedTask;
    }

    public IReadOnlyList<CapturedEmail> SentTo(string email) =>
        _sent.Where(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Token trong mail xác minh gần nhất gửi tới <paramref name="email"/> (so email không phân biệt hoa thường).</summary>
    public string LatestTokenFor(string email) =>
        SentTo(email).LastOrDefault()?.PlainToken
        ?? throw new InvalidOperationException($"Không có mail xác minh nào gửi tới {email}.");
}

public sealed record CapturedEmail(string ToEmail, string PlainToken);
