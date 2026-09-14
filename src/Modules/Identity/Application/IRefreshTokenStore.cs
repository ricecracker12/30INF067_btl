using System.Net;

namespace SocialApp.Modules.Identity.Application;

/// <summary>Bảng <c>refresh_tokens</c>. D3 chỉ phát token; D5 thêm xoay vòng, D6 thu hồi theo family.</summary>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Lưu token mới. <paramref name="tokenHash"/> là SHA-256 của bản rõ — bản rõ không bao giờ tới store (NFR-SEC-01).
    /// <paramref name="createdIp"/> chỉ để điều tra sau sự cố.
    /// </summary>
    Task CreateAsync(
        Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, IPAddress? createdIp, DateTimeOffset now,
        CancellationToken ct);
}
