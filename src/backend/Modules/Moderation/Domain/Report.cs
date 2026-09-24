namespace SocialApp.Modules.Moderation.Domain;

/// <summary>
/// Báo cáo vi phạm (ENT-12, bảng <c>moderation.reports</c>). Một người báo một <b>đối tượng</b> (bài, bình luận, người dùng)
/// kèm lý do chuẩn hóa.
///
/// Không FK và không navigation sang bảng nào (Đ-2.2): <see cref="ReporterId"/>, <see cref="ResolverId"/> là id người dùng của
/// Identity; <see cref="TargetId"/> đa hình, trỏ vào schema của Content hoặc Profile tùy <see cref="TargetType"/>.
///
/// Ràng buộc do DB giữ (<c>ReportConfiguration</c>): mỗi người một báo cáo MỞ cho mỗi đối tượng (index một phần);
/// <c>other</c> bắt buộc <see cref="Detail"/>; <c>open</c> ⇔ chưa có người quyết.
/// </summary>
public sealed class Report
{
    /// <summary>UUID v7 do app sinh (<c>Uuid7.New()</c>).</summary>
    public Guid Id { get; init; }

    public Guid ReporterId { get; init; }

    /// <summary>Một trong <see cref="ReportTargetTypes"/>.</summary>
    public required string TargetType { get; init; }

    public Guid TargetId { get; init; }

    /// <summary>Một trong <see cref="ReasonCodes"/>.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Mô tả của người báo, 1–500 ký tự; bắt buộc khi <see cref="ReasonCodes.Other"/>.</summary>
    public string? Detail { get; init; }

    /// <summary>Một trong <see cref="ReportStatus"/>.</summary>
    public string Status { get; set; } = ReportStatus.Open;

    /// <summary>Moderator/Admin đã quyết — có giá trị đúng khi <see cref="Status"/> khác <c>open</c>.</summary>
    public Guid? ResolverId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Ghi chú của người quyết, ≤ 500 ký tự.</summary>
    public string? ResolutionNote { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}
