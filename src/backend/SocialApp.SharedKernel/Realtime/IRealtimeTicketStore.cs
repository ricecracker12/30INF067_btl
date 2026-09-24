namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Danh tính mà một vé mang (Đ-5.9): <c>sub</c> + <c>role</c> để dựng principal ở hub, <c>iat</c> của access token ĐANG DÙNG lúc
/// xin vé để <c>revoked:user</c> so được về sau (Đ-5.10 — giữ <c>iat</c>, Đ-6.8 xác nhận).
/// </summary>
public sealed record RealtimeTicketClaims(string Sub, string Role, long Iat);

/// <summary>Vé vừa cấp — trả cho client đúng một lần. <see cref="ExpiresIn"/> tính bằng giây.</summary>
public sealed record RealtimeTicketIssue(string Ticket, int ExpiresIn);

/// <summary>
/// Kho vé realtime dùng một lần (Đ-E16, Đ-5.9). Vé KHÔNG bao giờ được lưu thô: khóa là băm SHA-256 của vé, nên người đọc được
/// Redis cũng không dùng lại được vé.
/// </summary>
public interface IRealtimeTicketStore
{
    /// <summary>
    /// Cấp một vé 32 byte ngẫu nhiên (base64url). Trả <c>null</c> khi Redis không sẵn sàng — người gọi trả 503, KHÔNG fail-open:
    /// không có chỗ lưu thì không có vé nào để kiểm.
    /// </summary>
    Task<RealtimeTicketIssue?> IssueAsync(RealtimeTicketClaims claims, CancellationToken ct = default);

    /// <summary>
    /// Đổi vé lấy danh tính — MỘT lần (<c>GETDEL</c>): lần thứ hai không còn gì để lấy (HUB-02). Vé sai, hết hạn, đã dùng, hay
    /// Redis không sẵn sàng đều trả <c>null</c>.
    /// </summary>
    Task<RealtimeTicketClaims?> RedeemAsync(string ticket, CancellationToken ct = default);
}
