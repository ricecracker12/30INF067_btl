using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Observability;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// <c>PATCH /reports/{reportId}</c> (GĐ6 D7c, Đ-6.13, UC-19). Thứ tự là cả thiết kế:
/// <list type="number">
/// <item><b>Tầng 2 kép</b> (L-D12): <c>hide</c> cần thêm <c>post.hide</c> ngoài <c>report.resolve</c> (đã qua ở attribute). Kiểm TRƯỚC
/// mọi I/O, chỉ phụ thuộc quyết định và vai trò — REVIEWER luôn nhận 403, không 404/409 tùy báo cáo, và không giữ khóa dòng trong lúc
/// tra cache quyền. Bị từ chối → một dòng <c>access.denied</c> (<c>tx: null</c>), target là báo cáo, <c>metadata.permission</c>.</item>
/// <item>Báo cáo tồn tại (không khóa) → bảng <c>decision × targetType</c> → ảnh chụp đối tượng (tác giả, bài cha cho event).</item>
/// <item>Store: transaction khóa-dòng → ẩn → đóng MỌI báo cáo mở → audit → <c>COMMIT</c>.</item>
/// <item>SAU <c>COMMIT</c>, ngoài transaction: metric, rồi <c>ContentHidden</c> khi bài VỪA bị ẩn (cạm bẫy 1, luật 5).</item>
/// </list>
/// Không so <c>role == "ADMIN"</c> (luật 3): <see cref="PermissionChecks.IsAllowedAsync"/> đã short-circuit.
/// </summary>
public sealed class DecideReportService(
    IModerationDecisionStore store,
    IModerationTargets targets,
    IPermissionCache permissions,
    IAuditTrail audit,
    IEventPublisher events,
    TimeProvider clock)
{
    /// <summary><c>targetStatus</c> khi đối tượng không còn trong bảng nào — cùng chữ với ảnh chụp của D7b.</summary>
    private const string GoneStatus = "deleted";

    /// <summary>Trạng thái bài/bình luận sau <c>hide</c> thành công (kể cả <c>AlreadyHidden</c>).</summary>
    private const string HiddenStatus = "hidden";

    /// <param name="actorRole">Claim <c>role</c> của token — controller đọc, không bao giờ từ body.</param>
    public async Task<Result<ReportDecisionResult>> DecideAsync(
        Guid reportId, Guid actorId, string? actorRole, DecideReportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decision = request.Decision!;

        if (decision == ReportDecision.Hide
            && !await permissions.IsAllowedAsync(actorRole, ModerationPermissions.PostHide, ct))
        {
            await audit.AppendAsync(null, new AuditEntry(
                actorId, AuditActions.AccessDenied, "report", reportId,
                new Dictionary<string, object?> { ["permission"] = ModerationPermissions.PostHide }), ct);
            return Result<ReportDecisionResult>.Forbidden();
        }

        var head = await store.FindReportAsync(reportId, ct);
        if (head is null)
            return ModerationErrors.ReportNotFound;

        var type = ReportTargetTypes.Parse(head.TargetType);
        if (!DecisionRules.IsAllowed(decision, type))
            return ModerationErrors.InvalidDecision;

        var target = new ModerationTarget(type, head.TargetId);
        var snapshot = targets.Supports(type)
            && (await targets.GetSnapshotsAsync([target], ct)).TryGetValue(target, out var found)
                ? found
                : null;

        // Không có gì để ẩn (loại chưa có provider, hoặc đối tượng biến mất khỏi mọi bảng) — dừng trước transaction. Đã xóa mềm thì còn
        // ảnh chụp, và HideAsync trả NotFound trong transaction → cùng 409.
        if (decision == ReportDecision.Hide && snapshot is null)
            return ModerationErrors.TargetGone;

        var reasonCode = request.ReasonCode ?? head.ReasonCode;
        var outcome = await store.DecideAsync(
            new ReportDecisionCommand(
                reportId, target, head.TargetType, decision, reasonCode,
                DecideReportRequestValidator.NormalizeNote(request.Note), actorId, clock.GetUtcNow()),
            ct);

        switch (outcome.Status)
        {
            case DecisionStatus.AlreadyDecided:
                return ModerationErrors.ReportAlreadyDecided;
            case DecisionStatus.TargetGone:
                return ModerationErrors.TargetGone;
        }

        // SAU COMMIT (store đã trả về). Không truyền ct vào đâu nữa: quyết định đã ghi.
        BusinessMetrics.ReportDecided(decision);

        // AlreadyHidden (báo cáo thứ hai cho bài đã ẩn): tác giả đã được báo ở lần ẩn đầu — không phát lại. Event không có ai ẩn:
        // thông báo moderation không lộ Moderator (cạm bẫy 6).
        if (outcome.Hide == HideOutcome.Hidden)
            events.Publish(new ContentHidden(type, target.Id, snapshot!.PostId, snapshot.AuthorId, reasonCode));

        return new ReportDecisionResult(
            decision,
            outcome.ClosedReportIds,
            decision == ReportDecision.Hide ? HiddenStatus : snapshot?.Status ?? GoneStatus);
    }
}
