using System.Text.Json;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Moderation.Application.Audit;

/// <summary>Một dòng nhật ký — <c>AuditLogItem</c> của hợp đồng.</summary>
/// <param name="Actor">Thẻ người thao tác, hydrate MỘT lô; <c>null</c> khi không có hồ sơ (Admin seed chưa onboarding).</param>
/// <param name="Metadata">JSON nguyên như lúc ghi — không bao giờ chứa nội dung người dùng (Đ-6.15, <c>AUD-01</c>).</param>
/// <param name="Ip">IP người thao tác dạng chuỗi; <c>null</c> khi không có request (job nền).</param>
public sealed record AuditLogItem(
    long Id,
    Guid ActorId,
    ModerationUserCard? Actor,
    string Action,
    string? TargetType,
    Guid? TargetId,
    JsonElement? Metadata,
    string? Ip,
    DateTimeOffset CreatedAt);

/// <param name="NextCursor"><c>null</c> khi hết dữ liệu.</param>
public sealed record AuditLogPage(IReadOnlyList<AuditLogItem> Items, string? NextCursor);

/// <summary>Một dòng như DB trả.</summary>
public sealed record AuditLogRow(
    long Id, Guid ActorId, string Action, string? TargetType, Guid? TargetId, string? Metadata, string? Ip, DateTimeOffset CreatedAt);

/// <param name="Before">Keyset: chỉ dòng có <c>id</c> nhỏ hơn.</param>
public sealed record AuditLogFilter(Guid? ActorId, string? Action, string? TargetType, Guid? TargetId, long? Before);

/// <summary>
/// Người đọc DUY NHẤT của bảng append-only (Đ-6.15). Hiện thực đọc <c>AsNoTracking</c> — một entity tracked mà ai đó
/// <c>SaveChanges</c> là trigger append-only ném (cạm bẫy 1).
/// </summary>
public interface IAuditLogQueries
{
    /// <param name="take">Người gọi truyền <c>limit + 1</c> để biết còn trang sau.</param>
    Task<IReadOnlyList<AuditLogRow>> ListAsync(AuditLogFilter filter, int take, CancellationToken ct);
}

/// <summary>
/// <c>GET /admin/audit-logs</c> (GĐ6 D8, ENT-13). Tầng 2 <c>audit.read</c> (chỉ ADMIN) và fail-closed ở controller. Số câu SQL mỗi trang
/// không phụ thuộc số dòng: một câu nhật ký + một lô <c>IUserDirectory</c> cho mọi người thao tác khác nhau của trang.
/// </summary>
public sealed class AuditLogReadService(IAuditLogQueries queries, IUserDirectory directory, IObjectStorage storage)
{
    /// <summary>Gọi SAU validator.</summary>
    public async Task<AuditLogPage> ListAsync(ListAuditLogsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        long? before = AuditLogCursor.TryDecode(query.Cursor, out var cursor) ? cursor.Id : null;
        var limit = query.EffectiveLimit;
        var rows = await queries.ListAsync(
            new AuditLogFilter(query.ActorId, query.Action, query.TargetType, query.TargetId, before), limit + 1, ct);

        var page = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        var next = rows.Count > limit ? new AuditLogCursor(page[^1].Id).Encode() : null;
        if (page.Count == 0)
            return new AuditLogPage([], next);

        var cards = await directory.GetManyAsync([.. page.Select(r => r.ActorId).Distinct()], ct);

        return new AuditLogPage(
            [.. page.Select(r => new AuditLogItem(
                r.Id,
                r.ActorId,
                cards.TryGetValue(r.ActorId, out var card)
                    ? new ModerationUserCard(
                        card.UserId, card.DisplayName, card.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null)
                    : null,
                r.Action,
                r.TargetType,
                r.TargetId,
                r.Metadata is null ? null : JsonDocument.Parse(r.Metadata).RootElement.Clone(),
                r.Ip,
                r.CreatedAt))],
            next);
    }
}
