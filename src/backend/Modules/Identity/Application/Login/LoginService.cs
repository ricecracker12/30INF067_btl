using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Observability;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Login;

/// <summary>
/// FR-002 + FR-003 theo ĐÚNG thứ tự 6 bước của giai-doan-1.md Mục 7.2 — thứ tự là hợp đồng, không phải chi tiết:
/// kiểm xác minh email trước mật khẩu thì ai cũng dò được email nào đã đăng ký mà chưa xác minh.
/// </summary>
public sealed class LoginService(
    IIdentityUserStore users,
    IRefreshTokenStore refreshTokens,
    IPasswordHasher hasher,
    IAccessTokenIssuer tokens,
    IOptions<JwtOptions> jwt,
    TimeProvider time,
    ILogger<LoginService> logger)
{
    public async Task<Result<LoginSuccess>> LoginAsync(LoginRequest request, IPAddress? ip, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // 1. Tra user theo email (citext — không phân biệt hoa thường).
        var user = await users.FindForLoginAsync(request.Email.Trim(), ct);

        // 2. Không tồn tại → VẪN tốn một phép BCrypt rồi mới trả. Bỏ dòng này là thời gian phản hồi lộ email có thật (AC-02).
        if (user is null)
        {
            hasher.VerifyAgainstDummy(request.Password);
            BusinessMetrics.LoginFailed();
            return IdentityErrors.InvalidCredentials;
        }

        // 3. Đang khóa → 423. Hợp đồng chấp nhận 423 lộ tài khoản tồn tại: người thật cần biết vì sao không vào được.
        if (user.LockedUntil > now)
            return IdentityErrors.Locked;

        // 4. Sai mật khẩu → tăng bộ đếm NGUYÊN TỬ trong DB; đủ 5 lần liên tiếp thì khóa và reset. Lần sai thứ 5 vẫn 401 —
        //    lần thử KẾ TIẾP mới thấy 423.
        if (!hasher.Verify(request.Password, user.PasswordHash))
        {
            await users.RegisterFailedLoginAsync(user.UserId, now, ct);
            BusinessMetrics.LoginFailed();
            return IdentityErrors.InvalidCredentials;   // CÙNG đối tượng lỗi với bước 2
        }

        // 5. Chưa xác minh → 403. Mật khẩu đúng nhưng KHÔNG reset bộ đếm và KHÔNG phát token.
        if (user.EmailVerifiedAt is null)
            return IdentityErrors.EmailNotVerified;

        // 6. Thành công: reset bộ đếm, family MỚI (một lần đăng nhập = một thiết bị), phát access + refresh.
        await users.ResetFailedLoginAsync(user.UserId, now, ct);
        var refresh = SecureToken.Generate();
        await refreshTokens.CreateAsync(user.UserId, familyId: Uuid7.New(), SecureToken.Hash(refresh),
            expiresAt: now.AddDays(jwt.Value.RefreshTokenDays), ip, now, ct);

        logger.LogInformation("Tài khoản {UserId} đăng nhập thành công", user.UserId);
        return new LoginSuccess(tokens.Issue(user.UserId, user.RoleCode), refresh);
    }
}
