using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Moderation.Application;

/// <summary>
/// Mọi <see cref="Error"/> của module Moderation ở MỘT chỗ — cùng nếp <c>SocialGraphErrors</c>: rà PII bằng một lần đọc (không
/// thông điệp nào chứa id, email hay nội dung), và một loại lỗi chỉ có đúng một cách nói. Thông điệp chép ĐÚNG câu trong
/// <c>example</c> của <c>moderation-v1.yaml</c> — FE hiện câu của server (luật frontend Mục 6).
///
/// D7 thêm ba 409 có <c>type</c> riêng (<c>report-already-decided</c>, <c>moderation-target-gone</c>, <c>moderation-not-hidden</c>)
/// vào đây, hằng <c>type</c> đặt cạnh <see cref="Error"/> (Mục 1.3 luật 8).
/// </summary>
public static class ModerationErrors
{
    /// <summary>
    /// 404 của <c>POST /reports</c> — MỘT lỗi cho bốn nhánh: loại chưa hỗ trợ (L-D13), không tồn tại, đã ẩn/xóa, và KHÔNG THẤY
    /// ĐƯỢC (Đ-6.12). Hai lỗi khác nhau cho "không tồn tại" và "không thấy" thì status code/thân lỗi tự nó tố cáo bài riêng tư id X
    /// có tồn tại — <c>REP-02</c>, <c>REP-IDOR</c> so cả <c>title</c>, <c>detail</c>, <c>type</c>.
    /// </summary>
    public static readonly Error ReportTargetNotFound =
        new("moderation.report_target_not_found", "Không tìm thấy nội dung cần báo cáo.", 404);

    /// <summary>
    /// 404 của <c>GET /reports/{reportId}</c> (D7b): báo cáo không tồn tại. Endpoint đặc quyền — người không có <c>report.resolve</c>
    /// dừng ở 403 trước khi tới đây, nên 404 không lộ gì cho người ngoài.
    /// </summary>
    public static readonly Error ReportNotFound = new("moderation.report_not_found", "Không tìm thấy báo cáo.", 404);

    /// <summary>400 <c>errors.targetId</c>: báo cáo bài/bình luận của chính mình. Kiểm SAU "thấy được" — xem service.</summary>
    public static Error SelfReportContent =>
        Error.Validation("targetId", "Không thể báo cáo nội dung của chính mình.");

    /// <summary>400 <c>errors.targetId</c>: báo cáo chính tài khoản mình (<c>targetType: user</c>).</summary>
    public static Error SelfReportUser =>
        Error.Validation("targetId", "Không thể báo cáo chính mình.");
}
