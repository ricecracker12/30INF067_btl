using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// <c>POST /reports</c> (FR-019, Đ-6.12, GĐ6 D6). Thứ tự kiểm là cả thiết kế:
/// <list type="number">
/// <item>Loại có provider không (L-D13) — bình luận trước GĐ3 → 404.</item>
/// <item>Tồn tại không (ảnh chụp).</item>
/// <item><b>Thấy được không</b> — theo đúng luật đọc của module chủ (<c>IModerationTargets.CanViewAsync</c>: BR-02 + đang
/// <c>published</c> với bài).</item>
/// <item>Của chính mình không → 400.</item>
/// </list>
/// Bốn nhánh đầu trả CÙNG <see cref="ModerationErrors.ReportTargetNotFound"/>: nếu "không thấy" khác "không tồn tại" thì endpoint
/// này thành máy dò "bài riêng tư id X có tồn tại không" (<c>REP-IDOR</c>). "Của mình" đứng SAU "thấy được" vì cùng lý do: bài
/// riêng tư của người khác phải ra 404 trước khi kịp so tác giả.
///
/// Không log <c>detail</c> (cạm bẫy 4 — nội dung người dùng tự gõ). Không audit: báo cáo là thao tác của người dùng thường, không
/// phải thao tác đặc quyền (Đ-6.15); dấu vết của nó là chính dòng <c>reports</c>.
/// </summary>
public sealed class ReportSubmissionService(IModerationTargets targets, IReportStore store, TimeProvider clock)
{
    public async Task<Result<(ReportReceipt Receipt, bool Created)>> SubmitAsync(
        Guid actorId, CreateReportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targetType = request.TargetType!;
        var target = new ModerationTarget(ReportTargetTypes.Parse(targetType), request.TargetId!.Value);

        if (!targets.Supports(target.Type))
            return ModerationErrors.ReportTargetNotFound;

        var snapshots = await targets.GetSnapshotsAsync([target], ct);
        if (!snapshots.TryGetValue(target, out var snapshot))
            return ModerationErrors.ReportTargetNotFound;

        if (!await targets.CanViewAsync(actorId, target, ct))
            return ModerationErrors.ReportTargetNotFound;

        if (snapshot.AuthorId == actorId)
            return target.Type == ModerationTargetType.User
                ? ModerationErrors.SelfReportUser
                : ModerationErrors.SelfReportContent;

        return await store.CreateOrGetOpenAsync(
            actorId, targetType, target.Id, request.ReasonCode!,
            CreateReportRequestValidator.NormalizeDetail(request.Detail), clock.GetUtcNow(), ct);
    }
}
