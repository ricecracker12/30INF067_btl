using System.Globalization;
using System.Text;
using FluentValidation;
using SocialApp.SharedKernel.Audit;

namespace SocialApp.Modules.Moderation.Application.Audit;

/// <summary>
/// Query string của <c>GET /admin/audit-logs</c> (<c>moderation-v1.yaml</c>). Class <c>[FromQuery]</c> để FluentValidation auto-validation
/// chạy — cùng nếp <c>ListReportsQuery</c>. Mọi bộ lọc tùy chọn và cộng dồn (AND).
/// </summary>
public sealed class ListAuditLogsQuery
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    /// <summary>Độ dài cột <c>target_type</c>.</summary>
    public const int MaxTargetTypeLength = 20;

    /// <summary>Người thao tác — đi theo <c>idx_audit_logs_actor</c>.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>
    /// Một trong <see cref="AuditActions.All"/>. Được đứng một mình (L-D14): bảng không index theo <c>action</c>, truy vấn đi theo PK lùi
    /// rồi lọc, <c>LIMIT</c> dừng sớm — chấp nhận được trên một bảng nhỏ, và "xem mọi <c>user.lock</c>" là câu hỏi hợp lệ của Admin.
    /// </summary>
    public string? Action { get; init; }

    /// <summary><c>post</c> · <c>comment</c> · <c>user</c> · <c>role</c> · <c>report</c> · <c>endpoint</c>… — đi theo <c>idx_audit_logs_target</c>.</summary>
    public string? TargetType { get; init; }

    /// <summary>Chỉ đi kèm <see cref="TargetType"/>: một uuid trần không nói nó là bài hay người, và index bắt đầu bằng <c>target_type</c>.</summary>
    public Guid? TargetId { get; init; }

    public string? Cursor { get; init; }

    /// <summary><c>int?</c>: <c>default(int)</c> là 0, nằm ngoài <c>1..100</c>, nên "không gửi limit" thành 400.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>Sai dạng → 400 theo trường, không sửa âm thầm.</summary>
public sealed class ListAuditLogsQueryValidator : AbstractValidator<ListAuditLogsQuery>
{
    public static readonly string LimitOutOfRange = $"Số dòng mỗi trang phải từ 1 đến {ListAuditLogsQuery.MaxLimit}.";

    public const string CursorInvalid = "Cursor không hợp lệ.";

    public const string ActionInvalid = "Hành động không hợp lệ.";

    public const string TargetIdNeedsType = "Lọc theo đối tượng cần có cả loại đối tượng.";

    public static readonly string TargetTypeTooLong = $"Loại đối tượng tối đa {ListAuditLogsQuery.MaxTargetTypeLength} ký tự.";

    public ListAuditLogsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ListAuditLogsQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || AuditLogCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);

        RuleFor(x => x.Action)
            .Must(a => a is null || AuditActions.All.Contains(a, StringComparer.Ordinal))
            .WithMessage(ActionInvalid);

        RuleFor(x => x.TargetType)
            .MaximumLength(ListAuditLogsQuery.MaxTargetTypeLength)
            .WithMessage(TargetTypeTooLong);

        RuleFor(x => x.TargetId)
            .Must((query, id) => id is null || !string.IsNullOrEmpty(query.TargetType))
            .WithMessage(TargetIdNeedsType);
    }
}

/// <summary>
/// Con trỏ keyset <c>id DESC</c>: <c>id</c> của dòng CUỐI trang trước. Một khóa là đủ — <c>id</c> là identity, không hòa. Opaque
/// (base64url của số) để client không suy luận hay tự tính id kế tiếp.
/// </summary>
public readonly record struct AuditLogCursor(long Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Id.ToString(CultureInfo.InvariantCulture)))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>Không ném với bất kỳ đầu vào nào; chỉ nhận số dương.</summary>
    public static bool TryDecode(string? raw, out AuditLogCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(raw))
            return false;

        var padded = raw.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        if (padded.Length % 4 != 0)
            return false;

        var buffer = new byte[padded.Length * 3 / 4];
        if (!Convert.TryFromBase64String(padded, buffer, out var written)
            || !long.TryParse(Encoding.UTF8.GetString(buffer, 0, written), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
            return false;

        cursor = new AuditLogCursor(id);
        return true;
    }
}
