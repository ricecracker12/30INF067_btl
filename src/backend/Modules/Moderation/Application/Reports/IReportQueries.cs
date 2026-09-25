namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Đường ĐỌC của bảng <c>moderation.reports</c> cho Moderator (GĐ6 D7b) — tách khỏi <see cref="IReportStore"/> (đường ghi) theo khuôn
/// <c>IAdminUserQueries</c> / <c>IAccountAdministrationStore</c> của Identity: hai đường không chung phụ thuộc nào, và fake của
/// <see cref="IReportStore"/> trong unit test D6 không phải hiện thực thêm hàm nó không dùng.
/// </summary>
public interface IReportQueries
{
    /// <summary>
    /// Một trang hàng đợi: báo cáo MỞ gom theo đối tượng, <c>first_reported_at ASC, target_id ASC</c>, sau <paramref name="after"/>.
    /// MỘT câu SQL cho cả trang, số lý do đếm trong chính câu đó.
    /// </summary>
    /// <param name="take">Người gọi truyền <c>limit + 1</c> để biết còn trang sau.</param>
    Task<IReadOnlyList<ReportQueueRow>> ListOpenAsync(ReportQueueCursor? after, int take, CancellationToken ct);

    /// <summary>
    /// Mọi báo cáo (mọi <c>status</c>) của CÙNG đối tượng với <paramref name="reportId"/>, <c>created_at ASC</c> — MỘT câu SQL.
    /// <c>null</c> khi báo cáo không tồn tại.
    /// </summary>
    Task<ReportThread?> FindThreadAsync(Guid reportId, CancellationToken ct);
}

/// <summary>Một dòng hàng đợi như DB trả — <see cref="ReportReadService"/> đổi thành <see cref="ReportQueueItem"/>.</summary>
public sealed record ReportQueueRow(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    int ReportCount,
    IReadOnlyDictionary<string, int> Reasons,
    DateTimeOffset FirstReportedAt);

/// <summary>Đối tượng của một báo cáo cùng mọi báo cáo về nó.</summary>
public sealed record ReportThread(string TargetType, Guid TargetId, IReadOnlyList<ReportRow> Reports);

/// <summary>
/// Một dòng <c>reports</c> cho chi tiết. <c>reporter_id</c> CỐ Ý không đọc lên: thứ không đọc thì không lọt ra response được.
/// </summary>
public sealed record ReportRow(
    Guid ReportId,
    string ReasonCode,
    string? Detail,
    string Status,
    Guid? ResolverId,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    DateTimeOffset CreatedAt);
