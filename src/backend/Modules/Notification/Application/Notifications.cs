using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using FluentValidation;

namespace SocialApp.Modules.Notification.Application;

/// <summary>Thẻ người gây ra sự kiện mới nhất — hydrate MỘT lô qua <c>IUserDirectory</c> lúc đọc, không lưu trong bảng (Đ-6.16).</summary>
/// <param name="AvatarUrl">Presigned GET, <c>null</c> khi chưa có ảnh đại diện.</param>
public sealed record NotificationUserCard(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>Đích FE dẫn tới: <c>post</c> · <c>comment</c> (kèm <paramref name="PostId"/>) · <c>user</c> · <c>conversation</c>.</summary>
public sealed record NotificationTarget(string Type, Guid Id, Guid? PostId);

/// <summary>
/// Một NHÓM thông báo — <c>NotificationResponse</c> của <c>notification-v1.yaml</c>. Không có câu hiển thị: FE ghép từ <c>type</c>, <c>actor</c>,
/// <c>actorCount</c> (Mục 8.3) — đổi chữ không phải đổi hợp đồng.
/// </summary>
/// <param name="Actor"><c>null</c> với <c>moderation</c> (không lộ ai kiểm duyệt), và khi người đó không có hồ sơ.</param>
/// <param name="ActorCount">Số người KHÁC NHAU trong đợt hiện tại (≥ 1).</param>
/// <param name="ReasonCode">Chỉ <c>moderation</c>.</param>
/// <param name="UpdatedAt">Lúc sự kiện mới nhất dồn vào nhóm — khóa sắp danh sách. Đánh dấu đã đọc KHÔNG đổi trường này.</param>
public sealed record NotificationResponse(
    Guid NotificationId,
    string Type,
    NotificationUserCard? Actor,
    int ActorCount,
    NotificationTarget Target,
    string? ReasonCode,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="NextCursor"><c>null</c> khi hết dữ liệu.</param>
public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, string? NextCursor);

/// <summary>Số NHÓM chưa đọc (không phải số sự kiện) — badge của chuông. FE hiện "9+" từ 10.</summary>
public sealed record UnreadNotificationCount(int Total);

/// <summary>
/// Body của <c>POST /notifications/read-all</c>. <see cref="UpTo"/> bắt buộc: chỉ đánh dấu nhóm có <c>updated_at ≤ upTo</c>, để thông báo
/// tới SAU lúc người dùng mở chuông không bị nuốt (Mục 8.3, cạm bẫy 3). <c>[Required]</c> chỉ để Swagger ghi <c>required</c> — luật thật là
/// validator (host tắt DataAnnotations).
/// </summary>
public sealed class ReadAllRequest
{
    [Required]
    public DateTimeOffset? UpTo { get; init; }
}

public sealed class ReadAllRequestValidator : AbstractValidator<ReadAllRequest>
{
    public const string UpToRequired = "Mốc thời gian là bắt buộc.";

    public ReadAllRequestValidator()
    {
        RuleFor(x => x.UpTo).NotNull().WithMessage(UpToRequired);
    }
}

/// <summary>Query string của <c>GET /notifications</c>: cursor mờ, <c>limit</c> 1..50 mặc định 20 (khuôn <c>ListConversationsQuery</c>).</summary>
public sealed class ListNotificationsQuery
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 50;

    public string? Cursor { get; init; }

    /// <summary><c>int?</c>: <c>default(int)</c> là 0, nằm ngoài <c>1..50</c>, nên "không gửi limit" thành 400.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>Sai dạng → 400 theo trường, không sửa âm thầm.</summary>
public sealed class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public static readonly string LimitOutOfRange = $"Số thông báo mỗi trang phải từ 1 đến {ListNotificationsQuery.MaxLimit}.";

    public const string CursorInvalid = "Cursor không hợp lệ.";

    public ListNotificationsQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, ListNotificationsQuery.MaxLimit).WithMessage(LimitOutOfRange);
        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || NotificationCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);
    }
}

/// <summary>
/// Con trỏ keyset <c>(updated_at, id)</c> sắp DESC — đúng <c>idx_notifications_recent</c>. Mờ với client: base64url của
/// <c>"{updatedAt:O}|{id:D}"</c> (khuôn <c>ConversationCursor</c> của Messaging — không import được, ADR-001).
///
/// <c>updated_at</c> ĐỔI khi nhóm có sự kiện mới, nên một nhóm có thể nhảy lên trang 1 lúc người dùng đang ở trang 2 và không hiện lại ở
/// trang 2 — chấp nhận (Mục 8.3); FE khử trùng theo <c>notificationId</c> khi nối trang.
/// </summary>
public readonly record struct NotificationCursor(DateTimeOffset UpdatedAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UpdatedAt:O}|{Id:D}"))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>Không ném với bất kỳ đầu vào nào — cursor là chuỗi client gửi lên (400, không phải 500).</summary>
    public static bool TryDecode(string? raw, out NotificationCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(raw) || raw.Length > 256)
            return false;

        var padded = raw.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        if (padded.Length % 4 != 0)
            return false;

        var buffer = new byte[padded.Length * 3 / 4];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
            return false;

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer, 0, written);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var parts = text.Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            || !Guid.TryParseExact(parts[1], "D", out var id))
            return false;

        // ToUniversalTime() bắt buộc: Npgsql từ chối DateTimeOffset có Offset khác 0 — 500 từ cursor sửa tay.
        cursor = new NotificationCursor(at.ToUniversalTime(), id);
        return true;
    }
}
