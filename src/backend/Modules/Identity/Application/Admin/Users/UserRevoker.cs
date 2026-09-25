using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Observability;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Ghi mốc <c>revoked:user</c> SAU khi DB đã <c>COMMIT</c> một thay đổi quyền (Đ-6.6) — khóa tài khoản (D3), đổi vai trò (D4).
/// Không bao giờ gọi trong transaction: Redis ghi xong mà DB rollback (409 last-admin) thì người bị "khóa hụt" vẫn 401 oan tới
/// 15 phút (cạm bẫy 4 của D3).
///
/// Redis hỏng đúng lúc này thì DB đã đổi, không lùi được: thử 3 lần trong ~1 giây (chờ 0 / 250 / 500 ms), vẫn hỏng → log
/// <b>Error</b> + <see cref="BusinessMetrics.RevocationFailed"/> → <see cref="RevocationStates.Deferred"/>. Không 503 (nói dối — thay
/// đổi đã lưu), không nuốt lỗi (thu hồi mất âm thầm). Chờ qua <see cref="TimeProvider"/> để unit test không ngủ thật.
///
/// Log chỉ mang <c>userId</c> — không email (B.10 #5, Mục 8.2).
/// </summary>
public sealed class UserRevoker(ITokenRevocationStore revocation, TimeProvider time, ILogger<UserRevoker> logger)
{
    /// <summary>Thời gian chờ TRƯỚC mỗi lần thử — ba lần, tổng ~0,75 giây.</summary>
    public static readonly TimeSpan[] Backoff = [TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];

    /// <summary>
    /// <see cref="RevocationStates.Applied"/> hoặc <see cref="RevocationStates.Deferred"/>. KHÔNG nhận CancellationToken của request:
    /// client ngắt kết nối sau <c>COMMIT</c> không được làm mất việc thu hồi (cùng lý do <c>SessionService.RevokeAccessTokensAsync</c>).
    /// Mốc thu hồi lấy ở lần thử thành công — mọi token phát trước đó, kể cả token chen giữa <c>COMMIT</c> và lúc này, đều chết.
    /// </summary>
    public async Task<string> RevokeAsync(Guid userId)
    {
        Exception? last = null;
        foreach (var delay in Backoff)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, time);

            try
            {
                await revocation.RevokeUserAsync(userId, time.GetUtcNow(), CancellationToken.None);
                return RevocationStates.Applied;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        logger.LogError(last, "Không ghi được mốc thu hồi cho tài khoản {UserId} sau {Attempts} lần thử", userId, Backoff.Length);
        BusinessMetrics.RevocationFailed();
        return RevocationStates.Deferred;
    }
}
