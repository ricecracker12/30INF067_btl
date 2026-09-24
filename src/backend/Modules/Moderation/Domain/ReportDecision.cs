namespace SocialApp.Modules.Moderation.Domain;

/// <summary>
/// Quyết định cho một báo cáo (Đ-6.13) — giá trị của <c>DecideReportRequest.decision</c>. Không phải cột DB: quyết định ghi
/// thành <see cref="ReportStatus"/> (<c>hide</c>/<c>resolve</c> → <c>resolved</c>, <c>dismiss</c> → <c>dismissed</c>) và thành
/// <c>action</c> của dòng audit.
///
/// Hợp lệ theo loại đối tượng (bảng <c>decision × targetType</c> của D7): <c>hide</c> cho bài/bình luận; <c>resolve</c> chỉ cho
/// người dùng và bắt buộc ghi chú; <c>dismiss</c> cho mọi loại.
/// </summary>
public static class ReportDecision
{
    public const string Hide = "hide";

    public const string Dismiss = "dismiss";

    public const string Resolve = "resolve";

    public static readonly string[] All = [Hide, Dismiss, Resolve];
}
