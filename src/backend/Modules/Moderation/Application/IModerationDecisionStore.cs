using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Moderation.Application;

/// <summary>
/// Hai thao tác GHI có transaction của Moderator (GĐ6 D7c): quyết định báo cáo (Đ-6.13) và khôi phục đối tượng. Store là chỗ DUY NHẤT
/// cầm transaction để truyền đi — <see cref="IModerationTargets"/> (ẩn/khôi phục bảng của module chủ) và <c>IAuditTrail</c> ghi trên
/// CHÍNH transaction đó (Đ-6.3), nên thay đổi + đóng báo cáo + audit cùng số phận (mốc 3). Cùng khuôn
/// <c>AccountAdministrationStore</c> của Identity (D3).
///
/// Tách khỏi <c>IReportStore</c> (đường báo cáo của người dùng, D6): phụ thuộc khác hẳn, và fake của unit test D6 không phải hiện thực
/// hàm nó không dùng.
/// </summary>
public interface IModerationDecisionStore
{
    /// <summary>Đọc KHÔNG khóa — để kiểm bảng hợp lệ và lấy ảnh chụp TRƯỚC khi mở transaction. <c>null</c> khi không tồn tại.</summary>
    Task<ReportHead?> FindReportAsync(Guid reportId, CancellationToken ct);

    /// <summary>
    /// Một transaction: khóa dòng báo cáo (<c>FOR UPDATE</c>) → không còn <c>open</c> thì dừng → <c>hide</c>: <c>HideAsync(tx)</c> → đóng
    /// MỌI báo cáo mở của đối tượng → audit(tx) → <c>COMMIT</c>. Dừng giữa chừng là không ghi gì.
    /// </summary>
    Task<DecisionOutcome> DecideAsync(ReportDecisionCommand command, CancellationToken ct);

    /// <summary>Một transaction: <c>RestoreAsync(tx)</c> → khác <c>Restored</c> thì dừng → audit <c>content.restore</c>(tx) → <c>COMMIT</c>.</summary>
    Task<RestoreOutcome> RestoreAsync(
        ModerationTarget target, string targetType, string? note, Guid actorId, CancellationToken ct);
}

/// <summary>Đúng những gì quyết định cần biết về một báo cáo trước khi khóa. Không có người báo.</summary>
public sealed record ReportHead(string TargetType, Guid TargetId, string ReasonCode);

/// <param name="TargetType">Chuỗi cột <c>target_type</c> (<c>ReportTargetTypes</c>).</param>
/// <param name="ReasonCode">Lý do đã chuẩn hóa: của request nếu có, không thì của báo cáo được mở.</param>
/// <param name="Note">Đã trim, khoảng trắng → <c>null</c>.</param>
public sealed record ReportDecisionCommand(
    Guid ReportId,
    ModerationTarget Target,
    string TargetType,
    string Decision,
    string ReasonCode,
    string? Note,
    Guid ActorId,
    DateTimeOffset Now);

public enum DecisionStatus
{
    /// <summary>Đã <c>COMMIT</c>.</summary>
    Decided,

    /// <summary>Báo cáo không còn <c>open</c> lúc khóa (US-019 AC-04, <c>MOD-C1</c>) → 409 <c>report-already-decided</c>.</summary>
    AlreadyDecided,

    /// <summary><c>HideAsync</c> trả <c>NotFound</c> (đối tượng đã xóa/biến mất) → 409 <c>moderation-target-gone</c>.</summary>
    TargetGone,
}

/// <param name="ClosedReportIds">Báo cáo vừa đóng, sắp theo id. Rỗng khi không <see cref="DecisionStatus.Decided"/>.</param>
/// <param name="Hide">Kết quả <c>HideAsync</c> khi quyết định là <c>hide</c>; <c>null</c> với quyết định khác.</param>
public sealed record DecisionOutcome(DecisionStatus Status, IReadOnlyList<Guid> ClosedReportIds, HideOutcome? Hide)
{
    public static DecisionOutcome Stopped(DecisionStatus status) => new(status, [], null);
}
