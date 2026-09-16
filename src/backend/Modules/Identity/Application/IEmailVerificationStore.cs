namespace SocialApp.Modules.Identity.Application;

/// <summary>Bảng <c>email_verification_tokens</c>. Hiện thực EF + SQL ở Infrastructure/Persistence (Đ-D1).</summary>
public interface IEmailVerificationStore
{
    /// <summary>
    /// Tiêu thụ token NGUYÊN TỬ và đánh dấu user đã xác minh, trong MỘT transaction. Hai request cùng token song song
    /// thì đúng một cái nhận <see cref="VerifyEmailOutcome.Verified"/>.
    /// </summary>
    Task<VerifyEmailOutcome> ConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Kết quả tiêu thụ token xác minh — ba nhánh ứng với 200 / 400 / 410 của hợp đồng.</summary>
public abstract record VerifyEmailOutcome
{
    private VerifyEmailOutcome()
    {
    }

    /// <summary>Token hợp lệ, đã tiêu thụ; user đã xác minh lúc <paramref name="VerifiedAt"/>.</summary>
    public sealed record Verified(Guid UserId, string Email, DateTimeOffset VerifiedAt) : VerifyEmailOutcome;

    /// <summary>Không có token nào mang băm này → 400 "liên kết không hợp lệ".</summary>
    public sealed record NotFound : VerifyEmailOutcome
    {
        public static readonly NotFound Instance = new();
    }

    /// <summary>Token có tồn tại nhưng đã hết hạn hoặc đã dùng → 410.</summary>
    public sealed record Gone : VerifyEmailOutcome
    {
        public static readonly Gone Instance = new();
    }
}
