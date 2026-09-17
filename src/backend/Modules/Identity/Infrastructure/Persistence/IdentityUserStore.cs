using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Application.Me;
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

    // LINQ thay vì SQL thô: EF gắn tham số theo kiểu của cột (citext) nên so không phân biệt hoa thường. SQL thô
    // `WHERE email = {email}` gửi tham số text và phân biệt hoa thường mà không lỗi (AC-01b bắt).
    public Task<LoginCandidate?> FindForLoginAsync(string email, CancellationToken ct) =>
        (from u in db.Users
         join r in db.Roles on u.RoleId equals r.RoleId
         where u.Email == email
         select new LoginCandidate(u.UserId, u.PasswordHash, r.Code, u.EmailVerifiedAt, u.LockedUntil))
        .SingleOrDefaultAsync(ct);

    public Task RegisterFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var max = LockoutPolicy.MaxFailedAttempts;
        var lockedUntil = now + LockoutPolicy.LockDuration;

        // MỘT câu lệnh: hai CASE đọc CÙNG giá trị cũ của failed_login_count nên không lệch nhau, và request song song xếp
        // hàng ở khóa dòng thay vì cùng đọc một giá trị rồi ghi đè nhau. updated_at tự set: SQL thô đi vòng ChangeTracker.
        return db.Database.ExecuteSqlAsync($"""
            UPDATE identity.users
               SET failed_login_count = CASE WHEN failed_login_count + 1 >= {max} THEN 0 ELSE failed_login_count + 1 END,
                   locked_until       = CASE WHEN failed_login_count + 1 >= {max} THEN {lockedUntil} ELSE locked_until END,
                   updated_at         = {now}
             WHERE user_id = {userId}
            """, ct);
    }

    // Điều kiện trong WHERE: tài khoản đã sạch (đa số lần đăng nhập) thì không ghi gì, updated_at đứng yên.
    public Task ResetFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE identity.users
               SET failed_login_count = 0, locked_until = NULL, updated_at = {now}
             WHERE user_id = {userId} AND (failed_login_count <> 0 OR locked_until IS NOT NULL)
            """, ct);

    // role VÀ roleDisplayName đọc từ DB, không từ claim trong token: từ GĐ6 hai giá trị có thể lệch tới 15 phút sau khi
    // Admin đổi vai trò, và /me phải nói sự thật hiện tại.
    public Task<MeResponse?> FindMeAsync(Guid userId, CancellationToken ct) =>
        (from u in db.Users
         join r in db.Roles on u.RoleId equals r.RoleId
         where u.UserId == userId
         select new MeResponse(u.UserId, u.Email, r.Code, r.DisplayName, u.EmailVerifiedAt, u.Status, u.CreatedAt))
        .SingleOrDefaultAsync(ct);

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
