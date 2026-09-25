using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Moderation.Application.Audit;

namespace SocialApp.Modules.Moderation.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IAuditLogQueries"/> — LINQ trên <see cref="ModerationDbContext.AuditLogs"/>, LUÔN <c>AsNoTracking</c> (cạm bẫy 1:
/// entity tracked + <c>SaveChanges</c> ở đâu đó là trigger append-only ném).
///
/// Keyset <c>id DESC</c>: <c>WHERE id &lt; @before ORDER BY id DESC LIMIT @take</c>. Lọc <c>actor_id</c> đi <c>idx_audit_logs_actor
/// (actor_id, id DESC)</c>; lọc <c>(target_type, target_id)</c> đi <c>idx_audit_logs_target</c>; <c>action</c> đứng một mình (L-D14) đi
/// theo PK lùi + lọc — bảng không index theo <c>action</c>, và <c>LIMIT</c> dừng sớm.
/// </summary>
internal sealed class AuditLogQueries(ModerationDbContext db) : IAuditLogQueries
{
    public async Task<IReadOnlyList<AuditLogRow>> ListAsync(AuditLogFilter filter, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var rows = db.AuditLogs.AsNoTracking();
        if (filter.ActorId is { } actor)
            rows = rows.Where(a => a.ActorId == actor);
        if (filter.Action is { } action)
            rows = rows.Where(a => a.Action == action);
        if (filter.TargetType is { } targetType)
            rows = rows.Where(a => a.TargetType == targetType);
        if (filter.TargetId is { } targetId)
            rows = rows.Where(a => a.TargetId == targetId);
        if (filter.Before is { } before)
            rows = rows.Where(a => a.Id < before);

        var page = await rows
            .OrderByDescending(a => a.Id)
            .Take(take)
            .Select(a => new { a.Id, a.ActorId, a.Action, a.TargetType, a.TargetId, a.Metadata, a.Ip, a.CreatedAt })
            .ToListAsync(ct);

        return [.. page.Select(a => new AuditLogRow(
            a.Id, a.ActorId, a.Action, a.TargetType, a.TargetId, a.Metadata, a.Ip?.ToString(), a.CreatedAt))];
    }
}
