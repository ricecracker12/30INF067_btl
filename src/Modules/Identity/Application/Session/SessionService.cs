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
                // Store đã COMMIT việc thu hồi family. D8 thêm thu hồi access token (revoked:user) ngay tại đây — DB trước,
                // Redis sau (Mục 7.5 cạm bẫy 1).
                logger.LogWarning("Phát hiện dùng lại refresh token: đã thu hồi cả family của tài khoản {UserId}", reuse.UserId);
                return IdentityErrors.SessionInvalid;

            default:
                return IdentityErrors.SessionInvalid;
        }
    }
}
