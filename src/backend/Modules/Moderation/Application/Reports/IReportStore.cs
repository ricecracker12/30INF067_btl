namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Bảng <c>moderation.reports</c>, đường GHI của NGƯỜI BÁO (D6). Hàng đợi và chi tiết (D7b) ở <see cref="IReportQueries"/>; quyết định và
/// khôi phục (D7c) ở <c>IModerationDecisionStore</c> — sửa 2026-09-25: bản đầu định dồn cả ba vào interface này.
/// </summary>
public interface IReportStore
{
    /// <summary>
    /// Một báo cáo MỞ mỗi (người báo, đối tượng) — Đ-6.12. Chèn nếu chưa có báo cáo mở; có rồi thì trả lại báo cáo đó
    /// (<c>Created = false</c>). Đúng dưới đồng thời: mười lượt song song ra MỘT dòng (<c>REP-C1</c>) — index một phần giữ luật,
    /// không phải lần đọc trước khi ghi.
    /// </summary>
    /// <param name="targetType">Chuỗi cột <c>target_type</c> (<c>ReportTargetTypes</c>).</param>
    /// <param name="detail">Đã chuẩn hóa (trim, khoảng trắng → null).</param>
    Task<(ReportReceipt Receipt, bool Created)> CreateOrGetOpenAsync(
        Guid reporterId, string targetType, Guid targetId, string reasonCode, string? detail, DateTimeOffset now,
        CancellationToken ct);
}
