namespace SocialApp.Modules.Moderation.Domain;

/// <summary>
/// Trạng thái báo cáo — khớp đúng <c>ck_reports_status</c> (giai-doan-6.md Mục 4). Một chiều: <c>open</c> → <c>resolved</c>
/// hoặc <c>dismissed</c>, không quay lại (Đ-6.13 — một quyết định, một lần).
///
/// <c>const string</c> chứ không enum, cùng nếp <c>UserStatus</c> của Identity: cột là <c>varchar</c> có CHECK, và chuỗi này
/// đi thẳng vào hợp đồng API (<c>ReportReceipt.status</c>).
/// </summary>
public static class ReportStatus
{
    /// <summary>Đang chờ xử lý — nằm trong hàng đợi.</summary>
    public const string Open = "open";

    /// <summary>Đã có quyết định <c>hide</c> hoặc <c>resolve</c>.</summary>
    public const string Resolved = "resolved";

    /// <summary>Quyết định <c>dismiss</c> — không vi phạm.</summary>
    public const string Dismissed = "dismissed";

    /// <summary>Ba giá trị hợp lệ — CHECK dựng từ mảng này.</summary>
    public static readonly string[] All = [Open, Resolved, Dismissed];
}
