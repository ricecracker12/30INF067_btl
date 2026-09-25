using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SocialApp.Modules.Identity.Application.Admin.Users;
using SocialApp.Modules.Identity.Domain;
using SocialApp.Modules.Identity.Infrastructure.Administration;
using SocialApp.SharedKernel.Audit;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IAccountAdministrationStore"/> (GĐ6 D3, D4). <see cref="IAuditTrail"/> inject vào ĐÂY chứ không vào service: store
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

    public async Task<AdminOutcome> AssignRoleAsync(
        Guid targetId, Guid actorId, string roleCode, bool actorCanManageRoles, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1. Khóa tư vấn LUÔN lấy — kể cả nâng ai đó LÊN ADMIN (L-D8, cạm bẫy 3 của D4).
        await AdminInvariant.AcquireAsync(db, ct);

        var target = await LockTargetAsync(targetId, ct);
        if (target is null)
            return AdminOutcome.NotFound;

        // 2. Tầng 2 kép, vế "người bị đổi ĐANG là ADMIN" (L-D18): chỉ biết được sau khi khóa dòng. Vế "vai trò đích là ADMIN" service
        //    đã chặn trước BEGIN. So mã vai trò của DỮ LIỆU — quyền của người gọi đã tra ở service qua IsAllowedAsync.
        if (target.RoleCode == RoleCodes.Admin && !actorCanManageRoles)
            return AdminOutcome.Forbidden;

        // 3. Vai trò đích tồn tại — so CHÍNH XÁC, phân biệt hoa thường (cạm bẫy 2): "user" không phải USER.
        var roleIds = await db.Roles.Where(r => r.Code == roleCode).Select(r => r.RoleId).ToListAsync(ct);
        if (roleIds.Count == 0)
            return AdminOutcome.UnknownRole;
        if (target.RoleCode == roleCode)
            return AdminOutcome.NoChange;   // L-D10: không ghi, không audit, không đụng Redis

        // 4. Ghi. KHÔNG đụng refresh_tokens (cạm bẫy 1): refresh cùng family phải cấp được token mang vai trò mới — người được đổi
        //    không bị đăng xuất (Mục 7.3). RotateAsync đọc vai trò từ DB lúc phát token.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE identity.users SET role_id = {roleIds[0]}, updated_at = {now} WHERE user_id = {targetId}", ct);

        // 5. Đếm SAU khi ghi — hạ Admin hoạt động cuối cùng → ROLLBACK.
        if (!await AdminInvariant.EnsureRemainsAsync(db, ct))
            return AdminOutcome.LastAdmin;

        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.RoleAssign, TargetType, targetId,
            new Dictionary<string, object?> { ["fromRole"] = target.RoleCode, ["toRole"] = roleCode }), ct);

        await tx.CommitAsync(CancellationToken.None);
        return AdminOutcome.Changed;
    }

    /// <summary>
    /// <c>FOR UPDATE OF u</c> dòng đích: login (bộ đếm sai, FR-003) cũng ghi <c>users</c> mà không lấy khóa tư vấn. Join <c>roles</c>
    /// để biết vai trò hiện tại (D4) — <c>OF u</c> chỉ khóa dòng tài khoản, không khóa dòng vai trò. ToListAsync, KHÔNG compose —
    /// cùng lý do <c>RotateAsync</c>: EF bọc câu thành subquery thì không chắc còn khóa đúng như câu gốc.
    /// </summary>
    private async Task<TargetRow?> LockTargetAsync(Guid targetId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<TargetRow>($"""
                SELECT u.status AS "Status", u.locked_until AS "LockedUntil", r.code AS "RoleCode"
                  FROM identity.users u JOIN identity.roles r ON r.role_id = u.role_id
                 WHERE u.user_id = {targetId}
                   FOR UPDATE OF u
                """)
            .ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private sealed record TargetRow(string Status, DateTimeOffset? LockedUntil, string RoleCode);
}
