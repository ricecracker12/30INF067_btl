namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>Đối tượng bị báo, dạng tham chiếu — <c>ReportTargetRef</c> của <c>moderation-v1.yaml</c>.</summary>
/// <param name="Type"><c>post</c> · <c>comment</c> · <c>user</c> (<c>ReportTargetTypes</c>).</param>
public sealed record ReportTargetRef(string Type, Guid Id);

/// <summary>
/// Một dòng hàng đợi kiểm duyệt = MỘT đối tượng, không phải một báo cáo (Đ-6.13): ba người báo cùng một bài là một dòng
/// <c>reportCount = 3</c>. Không có người báo, không có nội dung — mở chi tiết mới thấy.
/// </summary>
/// <param name="ReportId">Báo cáo MỞ cũ nhất của đối tượng — đại diện để mở <c>GET /reports/{reportId}</c>. Ổn định giữa các lần tải.</param>
/// <param name="Reasons">Số báo cáo mở theo lý do; chỉ lý do có ít nhất một báo cáo.</param>
/// <param name="FirstReportedAt">Báo cáo mở cũ nhất — khóa sắp xếp, cũ nhất trước.</param>
public sealed record ReportQueueItem(
    Guid ReportId,
    ReportTargetRef Target,
    int ReportCount,
    IReadOnlyDictionary<string, int> Reasons,
    DateTimeOffset FirstReportedAt);

/// <param name="NextCursor"><c>null</c> khi hết dữ liệu — không phải chuỗi rỗng.</param>
public sealed record ReportQueuePage(IReadOnlyList<ReportQueueItem> Items, string? NextCursor);
