using FluentValidation;
using SocialApp.Modules.Moderation.Domain;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Lớp "không cần I/O" của <c>POST /reports</c> (Đ-6.12). "Thấy được", "của chính mình" cần đối tượng — đó là việc của
/// <see cref="ReportSubmissionService"/>.
///
/// Tập giá trị so chính xác (phân biệt hoa thường) với ĐÚNG mảng mà CHECK của DB dựng từ đó: <c>"Post"</c> lọt qua đây thì CHECK
/// chặn muộn thành 500.
///
/// <c>detail</c> trim TRƯỚC khi đo (nếp <c>UpsertProfileRequestValidator</c>: validator trim khi đo, service trim khi lưu). Chỉ
/// khoảng trắng coi như VẮNG MẶT — nới hơn "1–500 khi có": với <c>spam</c> thì lưu <c>null</c>, với <c>other</c> thì cùng câu
/// "Vui lòng mô tả lý do." như khi thiếu hẳn.
/// </summary>
public sealed class CreateReportRequestValidator : AbstractValidator<CreateReportRequest>
{
    public const int MaxDetailLength = 500;

    public const string TargetTypeInvalid = "Loại đối tượng chỉ nhận post, comment hoặc user.";

    public const string TargetIdRequired = "Đối tượng cần báo cáo là bắt buộc.";

    public const string ReasonCodeInvalid = "Lý do báo cáo không hợp lệ.";

    public const string DetailRequired = "Vui lòng mô tả lý do.";

    public const string DetailTooLong = "Mô tả tối đa 500 ký tự.";

    public CreateReportRequestValidator()
    {
        RuleFor(x => x.TargetType)
            .Must(t => t is not null && ReportTargetTypes.All.Contains(t, StringComparer.Ordinal))
            .WithMessage(TargetTypeInvalid);

        RuleFor(x => x.TargetId)
            .Must(id => id is { } g && g != Guid.Empty)
            .WithMessage(TargetIdRequired);

        RuleFor(x => x.ReasonCode)
            .Must(r => r is not null && ReasonCodes.All.Contains(r, StringComparer.Ordinal))
            .WithMessage(ReasonCodeInvalid);

        RuleFor(x => x.Detail)
            .Must(d => d is null || d.Trim().Length <= MaxDetailLength)
            .WithMessage(DetailTooLong);

        // ck_reports_other_detail ở DB là lưới cuối; đây là câu người dùng đọc được.
        RuleFor(x => x.Detail)
            .Must(d => !string.IsNullOrWhiteSpace(d))
            .When(x => x.ReasonCode == ReasonCodes.Other)
            .WithMessage(DetailRequired);
    }

    /// <summary>Giá trị lưu: trim, chỉ khoảng trắng thành <c>null</c>. Service gọi — một chỗ cho luật "khoảng trắng = vắng mặt".</summary>
    public static string? NormalizeDetail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
}
