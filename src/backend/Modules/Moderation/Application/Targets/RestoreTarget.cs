using FluentValidation;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Moderation.Application.Targets;

/// <summary>Body TÙY CHỌN của <c>POST /moderation/targets/{targetType}/{targetId}/restore</c>.</summary>
public sealed class RestoreTargetRequest
{
    /// <summary>Ghi chú của người khôi phục, ≤ 500 sau khi trim. Vào audit <c>content.restore</c>, không vào log.</summary>
    public string? Note { get; init; }
}

public sealed class RestoreTargetRequestValidator : AbstractValidator<RestoreTargetRequest>
{
    public RestoreTargetRequestValidator()
    {
        RuleFor(x => x.Note)
            .Must(n => n is null || n.Trim().Length <= DecideReportRequestValidator.MaxNoteLength)
            .WithMessage(DecideReportRequestValidator.NoteTooLong);
    }
}

/// <summary>Body 200 của khôi phục — <c>ModerationTargetChange</c> của hợp đồng.</summary>
/// <param name="TargetStatus">Luôn <c>published</c> khi thành công.</param>
public sealed record ModerationTargetChange(string TargetType, Guid TargetId, string TargetStatus);

/// <summary>
/// Khôi phục đối tượng bị ẩn (FR-020, Đ-6.13). Tầng 2 <c>post.hide</c> ở attribute. Một transaction: <c>RestoreAsync(tx)</c> + audit
/// <c>content.restore</c>. <b>Không</b> mở lại báo cáo đã đóng, <b>không</b> phát event (không có loại thông báo "được khôi phục" —
/// Mục 13).
/// </summary>
public sealed class RestoreTargetService(IModerationDecisionStore store, IModerationTargets targets)
{
    private const string PublishedStatus = "published";

    /// <param name="targetType">Chuỗi trên đường — chưa qua validator nào (route param), nên kiểm ở đây.</param>
    public async Task<Result<ModerationTargetChange>> RestoreAsync(
        string targetType, Guid targetId, Guid actorId, RestoreTargetRequest? request, CancellationToken ct)
    {
        // Người dùng không "ẩn" được (Đ-6.13) nên không có gì để khôi phục. Chuỗi lạ cũng 400 dưới cùng trường.
        if (targetType is not (ReportTargetTypes.Post or ReportTargetTypes.Comment))
            return ModerationErrors.RestoreTypeInvalid;

        var target = new ModerationTarget(ReportTargetTypes.Parse(targetType), targetId);
        if (!targets.Supports(target.Type))
            return ModerationErrors.ModerationTargetNotFound;

        var outcome = await store.RestoreAsync(
            target, targetType, DecideReportRequestValidator.NormalizeNote(request?.Note), actorId, ct);

        return outcome switch
        {
            RestoreOutcome.Restored => new ModerationTargetChange(targetType, targetId, PublishedStatus),
            RestoreOutcome.NotHidden => ModerationErrors.TargetNotHidden,
            _ => ModerationErrors.ModerationTargetNotFound,
        };
    }
}
