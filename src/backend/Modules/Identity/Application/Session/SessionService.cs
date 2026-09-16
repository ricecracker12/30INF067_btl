using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Session;

/// <summary>
/// Phiên đăng nhập sau login: refresh (D5), logout (D6). Thuật toán xoay vòng nằm TRỌN trong
/// <see cref="IRefreshTokenStore.RotateAsync"/> (Đ-D1 — khóa dòng → quyết định → ghi phải trong một transaction); service chỉ
/// sinh token mới, phát access token và ánh xạ kết quả.
/// </summary>
public sealed class SessionService(
    IRefreshTokenStore refreshTokens,
    IAccessTokenIssuer tokens,
    ITokenRevocationStore revocation,
    IOptions<JwtOptions> jwt,
    TimeProvider time,
    ILogger<SessionService> logger)
{
    /// <summary>
    /// MỌI nhánh hỏng trả CÙNG <see cref="IdentityErrors.SessionInvalid"/>: phân biệt "hết hạn" với "bị thu hồi do reuse" là
    /// nói cho kẻ tấn công biết token nó cầm đang ở trạng thái nào.
    /// </summary>
    public async Task<Result<RefreshSuccess>> RefreshAsync(string? refreshCookie, IPAddress? ip, CancellationToken ct)
    {
        if (refreshCookie is null)
            return IdentityErrors.SessionInvalid;

        var now = time.GetUtcNow();
        var next = SecureToken.Generate();
        var outcome = await refreshTokens.RotateAsync(
            SecureToken.Hash(refreshCookie), now, SecureToken.Hash(next), now.AddDays(jwt.Value.RefreshTokenDays), ip, ct);

        switch (outcome)
        {
            case RotateOutcome.Rotated rotated:
                return new RefreshSuccess(tokens.Issue(rotated.UserId, rotated.RoleCode), next);

            case RotateOutcome.Grace grace:
                logger.LogInformation("Refresh trong ân hạn cho tài khoản {UserId}: phát token mới cùng family", grace.UserId);
                return new RefreshSuccess(tokens.Issue(grace.UserId, grace.RoleCode), next);

            case RotateOutcome.ReuseDetected reuse:
                // Store đã COMMIT việc thu hồi family — DB trước, Redis sau (Mục 7.5 cạm bẫy 1). Nghi bị đánh cắp → cắt cả access
                // token (bảng Mục 7.5). Mốc lấy SAU commit, không dùng `now` từ trước lượt xoay: token nào phát trong lúc store chạy
                // cũng nằm trước mốc.
                logger.LogWarning("Phát hiện dùng lại refresh token: đã thu hồi cả family của tài khoản {UserId}", reuse.UserId);
                await RevokeAccessTokensAsync(reuse.UserId);
                return IdentityErrors.SessionInvalid;

            default:
                return IdentityErrors.SessionInvalid;
        }
    }

    /// <summary>
    /// Redis lỗi thì log Error và vẫn trả 401: family đã thu hồi ở DB nên refresh đã bị chặn, chỉ access token còn sống tối đa
    /// 15 phút. KHÔNG truyền CancellationToken của request: client ngắt kết nối không được làm mất việc thu hồi (cùng lý do
    /// nhánh reuse của store COMMIT với CancellationToken.None).
    /// </summary>
    private async Task RevokeAccessTokensAsync(Guid userId)
    {
        try
        {
            await revocation.RevokeUserAsync(userId, time.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không ghi được revoked:user cho tài khoản {UserId}: access token hiện có sống tới hết hạn", userId);
        }
    }

    /// <summary>
    /// Đăng xuất THIẾT BỊ này: thu hồi cả family của cookie (giai-doan-1.md Mục 7.4). Không trả lỗi nào — logout idempotent
    /// (Đ-D6): cookie thiếu, lạ hay của người khác thì không làm gì. <paramref name="actorUserId"/> lấy từ access token, KHÔNG
    /// từ cookie (Mục 6.3). KHÔNG ghi revoked:user: key đó cắt access token của mọi thiết bị, trái bảng Mục 7.5.
    /// </summary>
    public async Task LogoutAsync(string? refreshCookie, Guid actorUserId, CancellationToken ct)
    {
        if (refreshCookie is null)
            return;

        var revoked = await refreshTokens.RevokeFamilyAsync(SecureToken.Hash(refreshCookie), actorUserId, time.GetUtcNow(), ct);
        if (revoked > 0)
            logger.LogInformation("Tài khoản {UserId} đăng xuất: thu hồi {Count} refresh token của thiết bị", actorUserId, revoked);
    }
}
