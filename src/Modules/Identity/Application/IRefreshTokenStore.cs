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

    /// <summary>
    /// Xoay vòng refresh token — TOÀN BỘ trong một transaction, khóa dòng bằng <c>SELECT … FOR UPDATE</c> (giai-doan-1.md Mục
    /// 7.3). Thuật toán nằm trọn trong store (Đ-D1): khóa → quyết định → ghi không được tách qua ranh giới tầng. Mọi nhánh
    /// COMMIT trước khi trả kết quả — kể cả <see cref="RotateOutcome.ReuseDetected"/>: rollback nhánh đó thì family không bị
    /// thu hồi mà endpoint vẫn 401. Vai trò đọc từ DB, không chép từ token cũ.
    /// </summary>
    Task<RotateOutcome> RotateAsync(
        string tokenHash, DateTimeOffset now, string newTokenHash, DateTimeOffset newExpiresAt, IPAddress? createdIp,
        CancellationToken ct);
}
