using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Application;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

internal sealed class EmailVerificationStore(IdentityDbContext db) : IEmailVerificationStore
{
    public async Task<VerifyEmailOutcome> ConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // MỘT câu UPDATE … RETURNING, không đọc-rồi-ghi: request thứ hai cùng token chờ khóa dòng, rồi đánh giá lại WHERE
        // trên dòng đã có consumed_at → không cập nhật được gì. Đọc trước thì cả hai cùng thấy NULL và cùng 200.
        // ToListAsync, KHÔNG compose (FirstOrDefault…): EF sẽ bọc thành SELECT … FROM (UPDATE …) và Postgres báo lỗi.
        // Alias "Value" bắt buộc cho SqlQuery kiểu scalar.
        var userIds = await db.Database.SqlQuery<Guid>($"""
            UPDATE identity.email_verification_tokens
               SET consumed_at = {now}
             WHERE token_hash = {tokenHash} AND consumed_at IS NULL AND expires_at > {now}
            RETURNING user_id AS "Value"
            """).ToListAsync(ct);

        if (userIds.Count == 0)
        {
            // Phân biệt 400 và 410 CHỈ SAU khi tiêu thụ thất bại: token không tồn tại mà trả 410 thì FE nói "link hết hạn"
            // cho một link bị gõ sai.
            var exists = await db.EmailVerificationTokens.AnyAsync(t => t.TokenHash == tokenHash, ct);
            return exists ? VerifyEmailOutcome.Gone.Instance : VerifyEmailOutcome.NotFound.Instance;
        }

        // SQL thô đi vòng ChangeTracker nên phải tự set updated_at (IdentityDbContext.StampUpdatedAt không thấy câu này).
        // RETURNING email: response cần email mà không tốn thêm một câu SELECT.
        var emails = await db.Database.SqlQuery<string>($"""
            UPDATE identity.users
               SET email_verified_at = {now}, updated_at = {now}
             WHERE user_id = {userIds[0]}
            RETURNING email::text AS "Value"
            """).ToListAsync(ct);

        await transaction.CommitAsync(ct);
        return new VerifyEmailOutcome.Verified(userIds[0], emails.Single(), now);
    }
}
