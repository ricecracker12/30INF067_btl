using FluentValidation;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Query string của <c>GET /admin/users</c> (<c>admin-v1.yaml</c>). Class <c>[FromQuery]</c> chứ không phải tham số rời:
/// FluentValidation auto-validation chỉ chạy trên model phức hợp — cùng nếp <c>ListUserPostsQuery</c>, <c>ListFriendsQuery</c>.
/// </summary>
public sealed class ListAdminUsersQuery
{
    public const int DefaultLimit = 20;

    /// <summary>Cùng trần các danh sách GĐ2/GĐ4 (AGENTS.md Mục 9).</summary>
    public const int MaxLimit = 50;

    /// <summary>Cột <c>users.email</c> là <c>varchar</c> theo RFC 5321 — dài hơn thì không email nào có tiền tố đó.</summary>
    public const int MaxQueryLength = 254;

    /// <summary>
    /// Tiền tố email, không phân biệt hoa thường (cột <c>citext</c>). <c>%</c>, <c>_</c>, <c>\</c> hiểu theo nghĩa đen
    /// (<c>LikePattern</c>). Không <c>Trim</c>: người gõ dấu cách đầu là đang tìm đúng chuỗi đó.
    /// </summary>
    public string? Q { get; init; }

    /// <summary><c>active</c> | <c>disabled</c>. <c>locked</c> không ai ghi, <c>deleted</c> chờ GĐ8 — không lọc được, không cần.</summary>
    public string? Status { get; init; }

    /// <summary>Mã vai trò (<c>roles.code</c>). Đúng dạng mà không tồn tại → trang rỗng, không 400: vai trò là dữ liệu từ GĐ6.</summary>
    public string? RoleCode { get; init; }

    public string? Cursor { get; init; }

    /// <summary><c>int?</c> chứ không <c>int</c>: <c>default(int)</c> là 0, nằm ngoài <c>1..50</c>, nên "không gửi limit" thành 400.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>
/// Sai dạng là 400 theo trường, không sửa âm thầm (cùng luật <c>ListUserPostsQueryValidator</c>). Thông điệp không nhắc lại giá trị
/// người dùng gửi — <c>q</c> có thể là một phần email.
/// </summary>
public sealed class ListAdminUsersQueryValidator : AbstractValidator<ListAdminUsersQuery>
{
    public static readonly string LimitOutOfRange =
        $"Số tài khoản mỗi trang phải từ 1 đến {ListAdminUsersQuery.MaxLimit}.";

    /// <summary>Câu của yaml. Không nói cursor sai chỗ nào: nó opaque.</summary>
    public const string CursorInvalid = "Cursor không hợp lệ.";

    public static readonly string QueryTooLong =
        $"Chuỗi tìm tối đa {ListAdminUsersQuery.MaxQueryLength} ký tự.";

    public const string StatusInvalid = "Trạng thái chỉ nhận active hoặc disabled.";

    public const string RoleCodeInvalid = "Mã vai trò không hợp lệ.";

    /// <summary>Hai trạng thái lọc được — hai giá trị Admin ghi được (Đ-6.5).</summary>
    private static readonly string[] FilterableStatuses = [UserStatus.Active, UserStatus.Disabled];

    public ListAdminUsersQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ListAdminUsersQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || AdminUserCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);

        RuleFor(x => x.Q)
            .MaximumLength(ListAdminUsersQuery.MaxQueryLength)
            .WithMessage(QueryTooLong);

        RuleFor(x => x.Status)
            .Must(s => s is null || FilterableStatuses.Contains(s, StringComparer.Ordinal))
            .WithMessage(StatusInvalid);

        RuleFor(x => x.RoleCode)
            .Must(code => code is null || IsRoleCodeShape(code))
            .WithMessage(RoleCodeInvalid);
    }

    /// <summary>
    /// <c>^[A-Z][A-Z0-9_]{2,29}$</c> — schema <c>RoleCode</c> của <c>identity-v1</c> (L-D16). So từng ký tự thay vì Regex: <c>$</c>
    /// của .NET khớp cả trước <c>"\n"</c> cuối chuỗi (bài học <c>VerifyEmailRequestValidator</c>).
    /// </summary>
    private static bool IsRoleCodeShape(string code) =>
        code.Length is >= 3 and <= 30
        && code[0] is >= 'A' and <= 'Z'
        && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
}
