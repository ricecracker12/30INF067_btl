namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Phản hồi của <c>POST /reports</c> — schema <c>ReportReceipt</c> của <c>moderation-v1.yaml</c>. 201 khi vừa tạo, 200 khi đã có
/// báo cáo MỞ của cùng người cho cùng đối tượng (trả lại báo cáo cũ, Đ-6.12).
///
/// CỐ Ý không trả lại <c>target</c> (Mục 8.1): không xác nhận thêm điều gì về đối tượng, và người báo không có <c>GET</c> nào để
/// xem lại báo cáo (không có <c>/reports/mine</c> ở MVP).
/// </summary>
/// <param name="Status">Luôn <c>open</c> — báo cáo trả về luôn là báo cáo đang mở.</param>
public sealed record ReportReceipt(Guid ReportId, string Status, DateTimeOffset CreatedAt);
