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
    /// </summary>
    Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default);
}
