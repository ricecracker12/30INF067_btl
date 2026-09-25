using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// <c>GET /reports</c>, <c>GET /reports/{reportId}</c> (GĐ6 D7b, UC-19). Tầng 2 (<c>report.resolve</c>) và fail-closed đã xong ở
/// controller; không có tầng 3 — endpoint đặc quyền, người ngoài đã bị chặn trước đó, nên 404 cho id không tồn tại không lộ gì.
///
/// Số câu SQL của chi tiết KHÔNG phụ thuộc số ảnh (<c>QUE-05</c>): một câu báo cáo + ảnh chụp C2 (một lô) + một lô
/// <c>IUserDirectory</c>. Ký URL là HMAC cục bộ, không phải I/O.
/// </summary>
public sealed class ReportReadService(
    IReportQueries queries,
    IModerationTargets targets,
    IUserDirectory directory,
    IObjectStorage storage)
{
    /// <summary><c>status</c> của ảnh chụp khi đối tượng không còn trong bảng nào — hợp đồng gộp nó với "đã xóa".</summary>
    public const string GoneStatus = "deleted";

    /// <summary>Gọi SAU validator: cursor giải mã được, limit trong <c>1..50</c>, status là <c>open</c> hoặc vắng.</summary>
    public async Task<ReportQueuePage> ListAsync(ListReportsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        ReportQueueCursor? after = ReportQueueCursor.TryDecode(query.Cursor, out var decoded) ? decoded : null;
        var limit = query.EffectiveLimit;
        var rows = await queries.ListOpenAsync(after, limit + 1, ct);

        var page = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        // Cursor từ dòng CUỐI của trang trả về, không từ dòng thừa thứ limit+1 — neo vào dòng thừa là bỏ sót đúng một dòng.
        var next = rows.Count > limit ? new ReportQueueCursor(page[^1].FirstReportedAt, page[^1].TargetId).Encode() : null;

        return new ReportQueuePage(
            [.. page.Select(r => new ReportQueueItem(
                r.ReportId, new ReportTargetRef(r.TargetType, r.TargetId), r.ReportCount, r.Reasons, r.FirstReportedAt))],
            next);
    }

    /// <summary>Báo cáo không tồn tại (mọi <c>status</c>) → 404. Báo cáo đã đóng vẫn mở được — Moderator xem lại lịch sử.</summary>
    public async Task<Result<ReportDetail>> GetAsync(Guid reportId, CancellationToken ct)
    {
        var thread = await queries.FindThreadAsync(reportId, ct);
        if (thread is null)
            return ModerationErrors.ReportNotFound;

        var target = new ModerationTarget(ReportTargetTypes.Parse(thread.TargetType), thread.TargetId);

        // Loại chưa có provider (bình luận trước GĐ3) không bao giờ có báo cáo — D6 trả 404 cho nó. Kiểm vẫn giữ: composite không
        // phải hợp đồng để dựa vào cho loại lạ, và nhánh này rẻ.
        var snapshots = targets.Supports(target.Type)
            ? await targets.GetSnapshotsAsync([target], ct)
            : new Dictionary<ModerationTarget, TargetSnapshot>();

        return new ReportDetail(
            reportId,
            snapshots.TryGetValue(target, out var snapshot)
                ? await ToViewAsync(thread.TargetType, snapshot, ct)
                : new ReportTargetSnapshot(thread.TargetType, thread.TargetId, GoneStatus, null, null, [], null, null, null),
            [.. thread.Reports
                .Where(r => r.Status == ReportStatus.Open)
                .Select(r => new OpenReport(r.ReportId, r.ReasonCode, r.Detail, r.CreatedAt))],
            History(thread.Reports));
    }

    /// <summary>MỘT lô <c>IUserDirectory</c> cho tác giả; URL ký sau khi đã qua tầng 2 (controller) — không bao giờ trong store.</summary>
    private async Task<ReportTargetSnapshot> ToViewAsync(string type, TargetSnapshot snapshot, CancellationToken ct)
    {
        var cards = await directory.GetManyAsync([snapshot.AuthorId], ct);
        var author = cards.TryGetValue(snapshot.AuthorId, out var card)
            ? new ModerationUserCard(
                card.UserId, card.DisplayName, card.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null)
            : null;

        return new ReportTargetSnapshot(
            type,
            snapshot.Target.Id,
            snapshot.Status,
            author,
            snapshot.Body,
            [.. snapshot.MediaKeys.Select(k => new ReportTargetMedia(storage.CreatePresignedGet(k)))],
            snapshot.PostId,
            snapshot.CreatedAt,
            snapshot.EditedAt);
    }

    /// <summary>
    /// Báo cáo đã đóng, gom theo lần quyết (L-D15): D7c đóng mọi báo cáo mở của đối tượng trong MỘT câu với cùng người, cùng mốc,
    /// cùng ghi chú — nên bộ bốn đó là khóa của một quyết định. Cũ nhất trước.
    /// </summary>
    private static IReadOnlyList<ReportHistoryEntry> History(IReadOnlyList<ReportRow> reports) =>
        [.. reports
            .Where(r => r.Status != ReportStatus.Open)
            // ck_reports_decided: báo cáo không mở LUÔN có resolver_id và resolved_at.
            .GroupBy(r => (r.Status, ResolverId: r.ResolverId!.Value, ResolvedAt: r.ResolvedAt!.Value, r.ResolutionNote))
            .OrderBy(g => g.Key.ResolvedAt)
            .Select(g => new ReportHistoryEntry(g.Key.Status, g.Key.ResolverId, g.Key.ResolvedAt, g.Key.ResolutionNote))];
}
