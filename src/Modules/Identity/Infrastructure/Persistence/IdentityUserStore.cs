using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

internal sealed class IdentityUserStore(IdentityDbContext db) : IIdentityUserStore
{
    // Tên index do migration InitialIdentity sinh. Chỉ unique violation của ĐÚNG index này mới là "email trùng" —
    // unique violation khác (token_hash) là lỗi thật, phải ra 500 chứ không được đổi thành 409.
    private const string EmailUniqueIndex = "IX_users_email";

    public async Task<short> GetRoleIdAsync(string roleCode, CancellationToken ct) =>
        await db.Roles.Where(r => r.Code == roleCode).Select(r => (short?)r.RoleId).SingleOrDefaultAsync(ct)
        ?? throw new InvalidOperationException(
            $"Không có vai trò '{roleCode}' trong bảng roles — dữ liệu nền chưa được nạp (chạy service migrate).");

    public async Task<bool> AddWithVerificationAsync(
        User user, EmailVerificationToken token, Func<Task> beforeCommit, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.Users.Add(user);
        db.EmailVerificationTokens.Add(token);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: EmailUniqueIndex,
        })
        {
            return false;   // dispose transaction = rollback
        }

        await beforeCommit();

        // Không truyền ct: mail đã đi, hủy commit lúc này chỉ để lại một link trỏ vào token không tồn tại.
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }
}
