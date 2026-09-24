using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SocialApp.Modules.Identity.Application.Admin.Users;
using SocialApp.Modules.Identity.Domain;
using SocialApp.Modules.Identity.Infrastructure.Administration;
using SocialApp.SharedKernel.Audit;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IAccountAdministrationStore"/> (GĐ6 D3). <see cref="IAuditTrail"/> inject vào ĐÂY chứ không vào service: store
/// là chỗ duy nhất cầm transaction để truyền đi (Đ-6.3) — dòng audit và thay đổi cùng số phận.
///
/// Không gọi Redis ở đây, không bao giờ (Đ-6.6, cạm bẫy 4 của D3). Không log <c>reason</c> (B.10 #5).
/// </summary>
internal sealed class AccountAdministrationStore(IdentityDbContext db, IAuditTrail audit) : IAccountAdministrationStore
{
    private const string TargetType = "user";

    public async Task<AdminOutcome> LockAsync(
        Guid targetId, Guid actorId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1. Khóa tư vấn LUÔN lấy (L-D8), TRƯỚC khóa dòng (cạm bẫy 2).
        await AdminInvariant.AcquireAsync(db, ct);

        // 2. Khóa dòng đích. Khóa tài khoản đã bị khóa (hay không còn active) → không đổi gì, không audit (L-D10).
        var target = await LockTargetAsync(targetId, ct);
        if (target is null)
            return AdminOutcome.NotFound;
        if (target.Status != UserStatus.Active)
            return AdminOutcome.NoChange;

        // 3. Ghi: disabled + thu hồi MỌI refresh family, mọi thiết bị (Đ-6.6). Token kế nhiệm mà một lượt refresh đang xoay chèn
        //    chen vào sau câu này thì sống sót ở đây — lưới trạng thái của RotateAsync (Đ-6.5, D1) chặn nó ở lần refresh kế tiếp.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE identity.users SET status = {UserStatus.Disabled}, updated_at = {now} WHERE user_id = {targetId}", ct);
        await db.RefreshTokens
            .Where(t => t.UserId == targetId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        // 4. Đếm SAU khi ghi. 0 Admin → ROLLBACK: dispose transaction chưa commit, không audit.
        if (!await AdminInvariant.EnsureRemainsAsync(db, ct))
            return AdminOutcome.LastAdmin;

        // 5. Audit trong CÙNG transaction. Không `revocation` (L-D9): thu hồi chạy sau COMMIT, lúc này chưa biết kết quả.
        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.UserLock, TargetType, targetId,
            new Dictionary<string, object?> { ["reason"] = reason }), ct);

        // Không truyền ct: đã tới đây thì mọi thứ đã ghi — hủy COMMIT lúc này chỉ để client đoán mò kết quả.
        await tx.CommitAsync(CancellationToken.None);
        return AdminOutcome.Changed;
    }

    public async Task<AdminOutcome> UnlockAsync(Guid targetId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Mở khóa không thể làm giảm số Admin, nhưng vẫn lấy khóa tư vấn: một luật, không ngoại lệ để nhớ (L-D8).
        await AdminInvariant.AcquireAsync(db, ct);

        var target = await LockTargetAsync(targetId, ct);
        if (target is null)
            return AdminOutcome.NotFound;

        // Hai loại khóa mở cùng lúc (Đ-6.5): Admin khóa (disabled) và khóa tạm FR-003 còn hiệu lực. Mốc FR-003 đã qua = đã tự mở.
        var disabled = target.Status == UserStatus.Disabled;
        var lockedOut = target.Status == UserStatus.Active && target.LockedUntil > now;
        if (!disabled && !lockedOut)
            return AdminOutcome.NoChange;

        await db.Database.ExecuteSqlAsync($"""
            UPDATE identity.users
               SET status = {UserStatus.Active}, locked_until = NULL, failed_login_count = 0, updated_at = {now}
             WHERE user_id = {targetId}
            """, ct);

        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.UserUnlock, TargetType, targetId), ct);

        await tx.CommitAsync(CancellationToken.None);
        return AdminOutcome.Changed;
    }

    /// <summary>
    /// <c>FOR UPDATE</c> dòng đích: login (bộ đếm sai, FR-003) cũng ghi <c>users</c> mà không lấy khóa tư vấn. ToListAsync, KHÔNG
    /// compose — cùng lý do <c>RotateAsync</c>: EF bọc câu thành subquery thì không chắc còn khóa đúng như câu gốc.
    /// </summary>
    private async Task<TargetRow?> LockTargetAsync(Guid targetId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<TargetRow>($"""
                SELECT status AS "Status", locked_until AS "LockedUntil"
                  FROM identity.users WHERE user_id = {targetId} FOR UPDATE
                """)
            .ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private sealed record TargetRow(string Status, DateTimeOffset? LockedUntil);
}
