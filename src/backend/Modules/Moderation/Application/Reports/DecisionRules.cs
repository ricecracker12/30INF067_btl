using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Bảng <c>decision × targetType</c> của Đ-6.13 — hàm thuần, chín ô có unit test (<c>DecisionRulesTests</c>):
///
/// <code>
///            post   comment   user
/// hide        ✓        ✓        ✗     người dùng không "ẩn" được — khóa tài khoản là việc của Admin
/// dismiss     ✓        ✓        ✓
/// resolve     ✗        ✗        ✓     chỉ cho người dùng, bắt buộc ghi chú (validator)
/// </code>
///
/// <c>hide</c> cho bình luận hợp lệ theo bảng; trước khi GĐ3 có provider bình luận thì không báo cáo bình luận nào tồn tại (D6 trả
/// 404), và service chặn thêm bằng <c>Supports</c> (L-D13).
/// </summary>
public static class DecisionRules
{
    public static bool IsAllowed(string decision, ModerationTargetType type) => (decision, type) switch
    {
        (ReportDecision.Hide, ModerationTargetType.Post or ModerationTargetType.Comment) => true,
        (ReportDecision.Dismiss, _) => true,
        (ReportDecision.Resolve, ModerationTargetType.User) => true,
        _ => false,
    };

    /// <summary><c>hide</c>/<c>resolve</c> → <c>resolved</c>, <c>dismiss</c> → <c>dismissed</c> (<see cref="ReportDecision"/>).</summary>
    public static string ReportStatusFor(string decision) =>
        decision == ReportDecision.Dismiss ? ReportStatus.Dismissed : ReportStatus.Resolved;

    /// <summary>Hằng <c>action</c> audit của quyết định (Đ-6.15).</summary>
    public static string AuditActionFor(string decision) => decision switch
    {
        ReportDecision.Hide => SharedKernel.Audit.AuditActions.ReportHide,
        ReportDecision.Dismiss => SharedKernel.Audit.AuditActions.ReportDismiss,
        ReportDecision.Resolve => SharedKernel.Audit.AuditActions.ReportResolve,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Quyết định không hợp lệ."),
    };
}
