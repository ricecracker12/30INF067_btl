using FluentValidation;
using SocialApp.Modules.Moderation.Domain;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Query string của <c>GET /reports</c> (<c>moderation-v1.yaml</c>). Class <c>[FromQuery]</c> để FluentValidation auto-validation chạy
/// — cùng nếp <c>ListAdminUsersQuery</c>.
/// </summary>
public sealed class ListReportsQuery
{
    public const int DefaultLimit = 20;

    /// <summary>Cùng trần các danh sách khác (AGENTS.md Mục 9).</summary>
    public const int MaxLimit = 50;

    /// <summary>
    /// Chỉ <c>open</c> (mặc định khi vắng). Giữ chỗ trong hợp đồng để thêm "đã xử lý" sau mà không đổi đường dẫn; giá trị khác hôm
    /// nay → 400, không âm thầm trả hàng đợi mở.
    /// </summary>
    public string? Status { get; init; }

    public string? Cursor { get; init; }

    /// <summary><c>int?</c>: <c>default(int)</c> là 0, nằm ngoài <c>1..50</c>, nên "không gửi limit" thành 400.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>Sai dạng → 400 theo trường, không sửa âm thầm. Thông điệp không nhắc lại giá trị người gửi.</summary>
public sealed class ListReportsQueryValidator : AbstractValidator<ListReportsQuery>
{
    public static readonly string LimitOutOfRange = $"Số dòng mỗi trang phải từ 1 đến {ListReportsQuery.MaxLimit}.";

    /// <summary>Câu của yaml. Không nói cursor sai chỗ nào: nó opaque.</summary>
    public const string CursorInvalid = "Cursor không hợp lệ.";

    public const string StatusInvalid = "Trạng thái chỉ nhận open.";

    public ListReportsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ListReportsQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || ReportQueueCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);

        // So chính xác, chữ thường — cùng luật chuỗi của CHECK ck_reports_status.
        RuleFor(x => x.Status)
            .Must(s => s is null || s == ReportStatus.Open)
            .WithMessage(StatusInvalid);
    }
}
