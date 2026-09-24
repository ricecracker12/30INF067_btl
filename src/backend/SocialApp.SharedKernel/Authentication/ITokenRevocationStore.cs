namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// Thu hồi access token theo user + <c>iat</c> (giai-doan-1.md Mục 7.5). Bên đọc: <c>OnTokenValidated</c> ở tầng 1. Bên ghi:
/// reuse detection (GĐ1), đổi vai trò / khóa tài khoản (GĐ6), xóa tài khoản (GĐ8). Thứ tự luôn là DB TRƯỚC, gọi hàm này SAU —
/// đảo lại thì token phát chen giữa hai bước mang dữ liệu cũ mà vẫn qua được mốc thu hồi (Mục 7.5 cạm bẫy 1).
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>
    /// Mọi access token của <paramref name="userId"/> phát TRƯỚC <paramref name="at"/> (so theo giây) bị từ chối. KHÔNG nuốt
    /// lỗi Redis: bên ghi thất bại phải lộ ra cho người gọi log — nuốt đi là thu hồi mất âm thầm.
    /// </summary>
    Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>
    /// true nếu token phát TRƯỚC mốc thu hồi: <c>iat &lt; mốc</c>, so CHẶT — đăng nhập lại trong cùng giây thu hồi không bị 401
    /// oan. Redis không sẵn sàng → false ngay (fail-open, Mục 7.5 "Khi Redis chết") + log warning.
    ///
    /// Hành vi GIỮ NGUYÊN từ GĐ1 — filter hub của GĐ5 gọi hàm này. Cần phân biệt "không kiểm được" thì dùng <see cref="CheckAsync"/>.
    /// </summary>
    Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default);

    /// <summary>
    /// Như <see cref="IsRevokedAsync"/> nhưng nói thật khi KHÔNG kiểm được: <see cref="RevocationCheck.Unknown"/> (Redis chưa kết
    /// nối, lỗi, quá hạn) — để endpoint đặc quyền fail-closed (Đ-6.8, GĐ6). Vẫn log warning fail-open có giới hạn tần suất như
    /// <see cref="IsRevokedAsync"/>: người gọi quyết định cho qua hay chặn, không phải quyết định có ghi log không.
    /// </summary>
    Task<RevocationCheck> CheckAsync(string userId, long issuedAtUnix, CancellationToken ct = default);
}

/// <summary>Kết quả kiểm thu hồi (Đ-6.8).</summary>
public enum RevocationCheck
{
    NotRevoked,
    Revoked,

    /// <summary>Kho thu hồi không trả lời được — endpoint thường fail-open, endpoint đặc quyền fail-closed.</summary>
    Unknown,
}
