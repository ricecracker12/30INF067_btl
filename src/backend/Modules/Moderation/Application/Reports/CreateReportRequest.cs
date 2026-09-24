using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Body của <c>POST /reports</c>, khớp schema <c>CreateReportRequest</c> của <c>moderation-v1.yaml</c>. Người báo là người gọi
/// (Mục 1.3 luật 2) — không nhận từ body.
///
/// <see cref="TargetType"/> và <see cref="ReasonCode"/> là <c>string</c>, không enum: <c>JsonStringEnumConverter</c> nhận cả
/// <c>"Post"</c> lẫn số nguyên <c>0</c>, và giá trị lạ vỡ ở bộ đọc JSON với câu chung chung. Chuỗi thì validator kiểm tập giá trị
/// bằng đúng mảng mà CHECK của DB dựng từ đó (<c>ReportTargetTypes.All</c>, <c>ReasonCodes.All</c>) và nói câu tiếng Việt dưới
/// đúng trường.
///
/// Class <c>init</c>, không từ khóa <c>required</c>: thiếu trường phải tới FluentValidation, không bị System.Text.Json chặn trước.
/// <c>[Required]</c> chỉ để Swagger ghi <c>required</c> — cổng hợp đồng so tập đó với yaml.
/// </summary>
public sealed class CreateReportRequest
{
    /// <summary><c>post</c> · <c>comment</c> · <c>user</c>.</summary>
    [Required]
    public string? TargetType { get; init; }

    [Required]
    public Guid? TargetId { get; init; }

    /// <summary>Một trong <c>ReasonCodes.All</c>.</summary>
    [Required]
    public string? ReasonCode { get; init; }

    /// <summary>1–500 ký tự sau trim; bắt buộc khi <c>other</c>. Nội dung người dùng tự gõ — không bao giờ vào log.</summary>
    public string? Detail { get; init; }
}
