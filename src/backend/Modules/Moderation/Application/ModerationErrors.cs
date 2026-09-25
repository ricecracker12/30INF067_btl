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

    /// <summary><c>type</c> của 409 "báo cáo đã được xử lý" (US-019 AC-04, <c>MOD-C1</c>) — FE làm mới hàng đợi.</summary>
    public const string ReportAlreadyDecidedType = "urn:socialapp:problem:report-already-decided";

    /// <summary><c>type</c> của 409 "đối tượng không còn để ẩn" — bài đã xóa hoặc biến mất giữa lúc mở và lúc quyết.</summary>
    public const string TargetGoneType = "urn:socialapp:problem:moderation-target-gone";

    /// <summary><c>type</c> của 409 "đối tượng đang không bị ẩn" của khôi phục.</summary>
    public const string TargetNotHiddenType = "urn:socialapp:problem:moderation-not-hidden";

    /// <summary>
    /// 409 của <c>PATCH /reports/{reportId}</c> (D7c): báo cáo không còn <c>open</c> lúc khóa dòng — Moderator khác vừa quyết, hoặc bấm
    /// lần hai. Không đổi gì.
    /// </summary>
    public static readonly Error ReportAlreadyDecided = new(
        "moderation.report_already_decided", "Báo cáo này đã được xử lý.", 409, Type: ReportAlreadyDecidedType);

    /// <summary>409 của <c>hide</c> khi không còn gì để ẩn — rollback, báo cáo vẫn mở để Moderator bỏ qua nó.</summary>
    public static readonly Error TargetGone = new(
        "moderation.target_gone", "Nội dung bị báo cáo không còn tồn tại.", 409, Type: TargetGoneType);

    /// <summary>409 của khôi phục khi đối tượng đang không bị ẩn.</summary>
    public static readonly Error TargetNotHidden = new(
        "moderation.target_not_hidden", "Nội dung này hiện không bị ẩn.", 409, Type: TargetNotHiddenType);

    /// <summary>400 <c>errors.decision</c>: sai ô của bảng <c>decision × targetType</c> (Đ-6.13) — cần báo cáo mới biết, nên ở service.</summary>
    public static Error InvalidDecision =>
        Error.Validation("decision", "Quyết định này không áp dụng cho loại nội dung bị báo cáo.");

    /// <summary>400 <c>errors.targetType</c> của khôi phục: chỉ bài và bình luận bị ẩn được — người dùng không "ẩn" được.</summary>
    public static Error RestoreTypeInvalid =>
        Error.Validation("targetType", "Chỉ khôi phục được bài viết hoặc bình luận.");

    /// <summary>404 của khôi phục: đối tượng không tồn tại (hoặc loại chưa có provider). Endpoint đặc quyền — không lộ gì cho người ngoài.</summary>
    public static readonly Error ModerationTargetNotFound =
        new("moderation.target_not_found", "Không tìm thấy nội dung.", 404);

    /// <summary>400 <c>errors.targetId</c>: báo cáo bài/bình luận của chính mình. Kiểm SAU "thấy được" — xem service.</summary>
    public static Error SelfReportContent =>
        Error.Validation("targetId", "Không thể báo cáo nội dung của chính mình.");

    /// <summary>400 <c>errors.targetId</c>: báo cáo chính tài khoản mình (<c>targetType: user</c>).</summary>
    public static Error SelfReportUser =>
        Error.Validation("targetId", "Không thể báo cáo chính mình.");
}
