using System.ComponentModel.DataAnnotations;
using FluentValidation;
using SocialApp.Modules.Moderation.Domain;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Body của <c>PATCH /reports/{reportId}</c> (<c>DecideReportRequest</c> của <c>moderation-v1.yaml</c>). Chuỗi chứ không enum — cùng lý
/// do <see cref="CreateReportRequest"/>: so chính xác chữ thường và báo lỗi tiếng Việt dưới đúng trường.
/// </summary>
public sealed class DecideReportRequest
{
    /// <summary>
    /// <c>hide</c> · <c>dismiss</c> · <c>resolve</c> (<see cref="ReportDecision"/>). <c>[Required]</c> chỉ để Swagger ghi <c>required</c> cho
    /// cổng hợp đồng — thiếu trường vẫn tới FluentValidation (khuôn <see cref="CreateReportRequest"/>).
    /// </summary>
    [Required]
    public string? Decision { get; init; }

    /// <summary>
    /// Lý do của quyết định. Vắng → lý do của báo cáo được mở. Có → dùng ĐÚNG giá trị này (cạm bẫy 5): FE mặc định lý do được báo
    /// nhiều nhất, Moderator có thể chọn khác. Với <c>hide</c> nó thành <c>hidden_reason</c> của bài và <c>reasonCode</c> của thông báo.
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>Ghi chú của Moderator, ≤ 500 sau khi trim; bắt buộc khi <c>resolve</c>. Vào <c>resolution_note</c> và audit, không vào log.</summary>
    public string? Note { get; init; }
}

/// <summary>Lớp "không cần I/O" của quyết định. Bảng <c>decision × targetType</c> cần báo cáo — việc của service.</summary>
public sealed class DecideReportRequestValidator : AbstractValidator<DecideReportRequest>
{
    public const int MaxNoteLength = 500;

    public const string DecisionInvalid = "Quyết định chỉ nhận hide, dismiss hoặc resolve.";

    public const string ReasonCodeInvalid = "Lý do không hợp lệ.";

    public const string NoteTooLong = "Ghi chú tối đa 500 ký tự.";

    public const string NoteRequired = "Vui lòng ghi chú cách đã xử lý.";

    public DecideReportRequestValidator()
    {
        RuleFor(x => x.Decision)
            .Must(d => d is not null && ReportDecision.All.Contains(d, StringComparer.Ordinal))
            .WithMessage(DecisionInvalid);

        RuleFor(x => x.ReasonCode)
            .Must(r => r is null || ReasonCodes.All.Contains(r, StringComparer.Ordinal))
            .WithMessage(ReasonCodeInvalid);

        RuleFor(x => x.Note)
            .Must(n => n is null || n.Trim().Length <= MaxNoteLength)
            .WithMessage(NoteTooLong);

        // resolve đóng báo cáo người dùng mà không ẩn gì — ghi chú là dấu vết duy nhất của "đã xử lý thế nào" (Đ-6.13).
        RuleFor(x => x.Note)
            .Must(n => !string.IsNullOrWhiteSpace(n))
            .When(x => x.Decision == ReportDecision.Resolve)
            .WithMessage(NoteRequired);
    }

    /// <summary>Giá trị lưu: trim, chỉ khoảng trắng thành <c>null</c> — cùng luật <c>detail</c> của D6.</summary>
    public static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
