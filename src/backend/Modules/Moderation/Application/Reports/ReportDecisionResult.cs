namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>Body 200 của <c>PATCH /reports/{reportId}</c> — <c>ReportDecisionResult</c> của hợp đồng.</summary>
/// <param name="ClosedReportIds">
/// MỌI báo cáo mở của đối tượng vừa đóng cùng quyết định này (Đ-6.13 bước 3), sắp theo id — FE bỏ chúng khỏi hàng đợi.
/// </param>
/// <param name="TargetStatus">
/// Trạng thái đối tượng sau quyết định: <c>hide</c> → <c>hidden</c>; còn lại → trạng thái của ảnh chụp lúc quyết
/// (<c>published</c> · <c>deleted</c> · <c>active</c> · <c>disabled</c>).
/// </param>
public sealed record ReportDecisionResult(string Decision, IReadOnlyList<Guid> ClosedReportIds, string TargetStatus);
