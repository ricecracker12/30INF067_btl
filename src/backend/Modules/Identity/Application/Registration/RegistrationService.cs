using Microsoft.Extensions.Logging;
using SocialApp.Modules.Identity.Application.Email;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>FR-001: tạo tài khoản chưa xác minh + gửi mail xác minh (D1), rồi xác minh bằng token trong mail (D2).</summary>
public sealed class RegistrationService(
    IIdentityUserStore users,
    IEmailVerificationStore verifications,
    IPasswordHasher hasher,
    IEmailSender email,
    TimeProvider time,
    ILogger<RegistrationService> logger)
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    public async Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var user = new User
        {
            // KHÔNG ToLowerInvariant: cột citext lo phần hoa thường, người dùng thấy lại đúng cách họ gõ.
            Email = request.Email.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            RoleId = await users.GetRoleIdAsync(RoleCodes.User, ct),
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Bản rõ chỉ đi vào mail; DB giữ băm (NFR-SEC-01).
        var plainToken = SecureToken.Generate();
        var token = new EmailVerificationToken
        {
            UserId = user.UserId,
            TokenHash = SecureToken.Hash(plainToken),
            ExpiresAt = now + VerificationLifetime,
        };

        // Gửi mail TRƯỚC commit (Đ-D5): GĐ1 không có endpoint gửi lại mail — commit trước rồi SMTP hỏng là tài khoản
        // kẹt vĩnh viễn (không xác minh được, đăng ký lại thì 409).
        var created = await users.AddWithVerificationAsync(user, token,
            beforeCommit: () => email.SendVerificationAsync(user.Email, plainToken, ct), ct);

        if (!created)
            return IdentityErrors.EmailTaken;

        logger.LogInformation("Đã đăng ký tài khoản {UserId} và gửi mail xác minh", user.UserId);
        return new RegisterResponse(user.UserId, user.Email);
    }

    /// <summary>FR-001 nửa sau. Token sai định dạng đã bị validator chặn (400 có <c>errors</c>) trước khi tới đây.</summary>
    public async Task<Result<VerifyEmailResponse>> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        var outcome = await verifications.ConsumeAsync(SecureToken.Hash(request.Token), time.GetUtcNow(), ct);

        switch (outcome)
        {
            case VerifyEmailOutcome.Verified verified:
                logger.LogInformation("Tài khoản {UserId} đã xác minh email", verified.UserId);
                return new VerifyEmailResponse(verified.Email, verified.VerifiedAt);
            case VerifyEmailOutcome.Gone:
                return IdentityErrors.VerifyTokenGone;
            default:
                return IdentityErrors.VerifyTokenInvalid;
        }
    }
}
