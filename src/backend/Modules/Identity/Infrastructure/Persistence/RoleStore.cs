using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SocialApp.Modules.Identity.Application.Roles;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Audit;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IRoleStore"/> (GĐ6 D5). Audit ghi trong CÙNG transaction qua <see cref="IAuditTrail"/> (Đ-6.3) với
/// <c>target_type = role</c>, <c>target_id = null</c> — cột là <c>uuid</c>, <c>role_id</c> là <c>smallint</c>; mã vai trò nằm ở
/// <c>metadata.code</c> (bảng Đ-6.15).
///
/// "Vai trò hệ thống" nhận bằng MÃ (<see cref="RoleCodes.All"/>), không bằng <c>role_id ≤ 3</c> — id là chi tiết seed (cạm bẫy 5).
/// </summary>
internal sealed class RoleStore(IdentityDbContext db, IAuditTrail audit) : IRoleStore
{
    private const string TargetType = "role";
    private const string CodeUniqueIndex = "IX_roles_code";

    public async Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.RoleId)
            .Select(r => new { r.RoleId, r.Code, r.DisplayName })
            .ToListAsync(ct);
        var counts = await UserCountsAsync(null, ct);
        var grants = await GrantsAsync(null, ct);

        return [.. roles.Select(r => Summary(r.RoleId, r.Code, r.DisplayName, counts, grants))];
    }

    public async Task<RoleSummary?> FindAsync(short roleId, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking()
            .Where(r => r.RoleId == roleId)
            .Select(r => new { r.RoleId, r.Code, r.DisplayName })
            .SingleOrDefaultAsync(ct);
        if (role is null)
            return null;

        return Summary(role.RoleId, role.Code, role.DisplayName,
            await UserCountsAsync(roleId, ct), await GrantsAsync(roleId, ct));
    }

    public async Task<IReadOnlyList<PermissionInfo>> ListPermissionsAsync(CancellationToken ct) =>
        [.. (await db.Permissions.AsNoTracking()
                .OrderBy(p => p.PermissionId)
                .Select(p => new { p.Code, p.Description })
                .ToListAsync(ct))
            .Select(p => new PermissionInfo(p.Code, p.Description, RoleRules.AssignablePermissions.Contains(p.Code)))];

    public async Task<RoleWriteResult> CreateAsync(
        string code, string displayName, IReadOnlySet<string> permissions, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var roleId = await db.Database
            .SqlQuery<short>($"SELECT nextval('identity.roles_role_id_seq')::smallint AS \"Value\"")
            .SingleAsync(ct);
        try
        {
            // SQL thô, không Add + SaveChanges: DbContext scoped, entity bị từ chối vì trùng mã sẽ nằm lại ChangeTracker.
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO identity.roles (role_id, code, display_name) VALUES ({roleId}, {code}, {displayName})", ct);
        }
        catch (PostgresException ex) when (ex is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CodeUniqueIndex })
        {
            return new RoleWriteResult(RoleWriteStatus.CodeTaken);   // dispose = rollback; sequence mất một số, chấp nhận
        }

        var added = permissions.Order(StringComparer.Ordinal).ToArray();
        await InsertGrantsAsync(roleId, added, ct);

        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.RoleCreate, TargetType, null,
            new Dictionary<string, object?> { ["code"] = code, ["added"] = added }), ct);

        await tx.CommitAsync(CancellationToken.None);
        return new RoleWriteResult(RoleWriteStatus.Changed, roleId, code);
    }

    public async Task<RoleWriteResult> RenameAsync(short roleId, string displayName, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var role = await LockRoleAsync(roleId, ct);
        if (role is null)
            return new RoleWriteResult(RoleWriteStatus.NotFound);
        if (role.DisplayName == displayName)
            return new RoleWriteResult(RoleWriteStatus.NoChange, roleId, role.Code);

        // Trigger A3 là BEFORE UPDATE OF code — đổi display_name của vai trò hệ thống đi qua (ROLE-05).
        await db.Database.ExecuteSqlAsync(
            $"UPDATE identity.roles SET display_name = {displayName}, updated_at = now() WHERE role_id = {roleId}", ct);

        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.RoleRename, TargetType, null,
            new Dictionary<string, object?> { ["code"] = role.Code, ["from"] = role.DisplayName, ["to"] = displayName }), ct);

        await tx.CommitAsync(CancellationToken.None);
        return new RoleWriteResult(RoleWriteStatus.Changed, roleId, role.Code);
    }

    public async Task<RoleWriteResult> SetPermissionsAsync(
        short roleId, IReadOnlySet<string> permissions, bool confirmed, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Khóa dòng vai trò: hai Admin sửa cùng vai trò thì diff của người sau tính trên tập người trước đã ghi.
        var role = await LockRoleAsync(roleId, ct);
        if (role is null)
            return new RoleWriteResult(RoleWriteStatus.NotFound);
        if (role.Code == RoleCodes.Admin)
            return new RoleWriteResult(RoleWriteStatus.SystemRole, roleId, role.Code);   // ADMIN không có dòng nào để sửa

        var current = (await GrantsAsync(roleId, ct)).GetValueOrDefault(roleId, []);
        var diff = RolePermissionDiff.Compute(new HashSet<string>(current, StringComparer.Ordinal), permissions);
        if (!diff.HasChanges)
            return new RoleWriteResult(RoleWriteStatus.NoChange, roleId, role.Code);

        // Mọi kiểm TRƯỚC mọi ghi (cạm bẫy 4): 409 xác nhận tính trên DB chưa bị đụng tới.
        var isSystem = RoleCodes.All.Contains(role.Code, StringComparer.Ordinal);
        if (isSystem && diff.IsEmptyAfter)
            return new RoleWriteResult(RoleWriteStatus.NeedsPermission, roleId, role.Code);
        if (isSystem && !confirmed)
        {
            var affected = await db.Users.CountAsync(u => u.RoleId == roleId, ct);
            return new RoleWriteResult(RoleWriteStatus.ConfirmationRequired, roleId, role.Code, diff, affected);
        }

        if (diff.Removed.Count > 0)
        {
            var removed = diff.Removed.ToArray();
            await db.Database.ExecuteSqlAsync($"""
                DELETE FROM identity.role_permissions rp
                 USING identity.permissions p
                 WHERE rp.permission_id = p.permission_id AND rp.role_id = {roleId} AND p.code = ANY({removed})
                """, ct);
        }
        await InsertGrantsAsync(roleId, [.. diff.Added], ct);

        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.RolePermissions, TargetType, null,
            new Dictionary<string, object?>
            {
                ["code"] = role.Code,
                ["added"] = diff.Added,
                ["removed"] = diff.Removed,
                ["confirmed"] = confirmed,
            }), ct);

        await tx.CommitAsync(CancellationToken.None);
        return new RoleWriteResult(RoleWriteStatus.Changed, roleId, role.Code);
    }

    public async Task<RoleWriteResult> DeleteAsync(short roleId, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var role = await LockRoleAsync(roleId, ct);
        if (role is null)
            return new RoleWriteResult(RoleWriteStatus.NotFound);
        if (RoleCodes.All.Contains(role.Code, StringComparer.Ordinal))
            return new RoleWriteResult(RoleWriteStatus.SystemRole, roleId, role.Code);   // lớp 2; trigger A3 là lớp 3
        if (await db.Users.AnyAsync(u => u.RoleId == roleId, ct))
            return new RoleWriteResult(RoleWriteStatus.InUse, roleId, role.Code);

        try
        {
            // role_permissions đi theo (ON DELETE CASCADE). Gán vai trò chen giữa lần đếm và câu này thì FK RESTRICT chặn.
            await db.Database.ExecuteSqlAsync($"DELETE FROM identity.roles WHERE role_id = {roleId}", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new RoleWriteResult(RoleWriteStatus.InUse, roleId, role.Code);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
        {
            return new RoleWriteResult(RoleWriteStatus.SystemRole, roleId, role.Code);
        }

        await audit.AppendAsync(tx.GetDbTransaction(), new AuditEntry(
            actorId, AuditActions.RoleDelete, TargetType, null,
            new Dictionary<string, object?> { ["code"] = role.Code }), ct);

        await tx.CommitAsync(CancellationToken.None);
        return new RoleWriteResult(RoleWriteStatus.Changed, roleId, role.Code);
    }

    // --- Dùng chung -----------------------------------------------------------------------------------------------------------

    private async Task<LockedRole?> LockRoleAsync(short roleId, CancellationToken ct) =>
        (await db.Database
            .SqlQuery<LockedRole>($"""
                SELECT code AS "Code", display_name AS "DisplayName"
                  FROM identity.roles WHERE role_id = {roleId} FOR UPDATE
                """)
            .ToListAsync(ct))
        .SingleOrDefault();

    private Task InsertGrantsAsync(short roleId, string[] codes, CancellationToken ct) =>
        codes.Length == 0
            ? Task.CompletedTask
            : db.Database.ExecuteSqlAsync($"""
                INSERT INTO identity.role_permissions (role_id, permission_id)
                SELECT {roleId}, permission_id FROM identity.permissions WHERE code = ANY({codes})
                """, ct);

    /// <summary>Số tài khoản theo vai trò — MỌI trạng thái (tài khoản bị khóa mở lại vẫn mang quyền).</summary>
    private async Task<Dictionary<short, int>> UserCountsAsync(short? roleId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => roleId == null || u.RoleId == roleId)
            .GroupBy(u => u.RoleId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    /// <summary>Mã quyền theo vai trò, thứ tự <c>permission_id</c> — cùng thứ tự <see cref="PermissionCodes.All"/> mà ADMIN nhận.</summary>
    private async Task<Dictionary<short, List<string>>> GrantsAsync(short? roleId, CancellationToken ct) =>
        (await (from rp in db.RolePermissions
                join p in db.Permissions on rp.PermissionId equals p.PermissionId
                where roleId == null || rp.RoleId == roleId
                orderby p.PermissionId
                select new { rp.RoleId, p.Code })
            .ToListAsync(ct))
        .GroupBy(x => x.RoleId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToList());

    private static RoleSummary Summary(
        short roleId, string code, string displayName, Dictionary<short, int> counts, Dictionary<short, List<string>> grants) =>
        new(roleId, code, displayName,
            IsSystem: RoleCodes.All.Contains(code, StringComparer.Ordinal),
            Editable: code != RoleCodes.Admin,
            UserCount: counts.GetValueOrDefault(roleId),
            Permissions: EffectivePermissions.For(code, grants.GetValueOrDefault(roleId, [])));

    private sealed record LockedRole(string Code, string DisplayName);
}
