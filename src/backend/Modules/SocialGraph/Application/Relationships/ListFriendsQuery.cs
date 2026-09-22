using FluentValidation;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Query string của <c>GET /friends</c>. Class <c>[FromQuery]</c> chứ không phải tham số rời: FluentValidation
/// auto-validation chỉ chạy trên model phức hợp — cùng nếp <c>ListUserPostsQuery</c>.
/// </summary>
public sealed class ListFriendsQuery
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 50;

    public string? Cursor { get; init; }

    /// <summary><c>int?</c> chứ không <c>int</c>: <c>default(int)</c> là 0, nằm ngoài <c>1..50</c>, nên "không gửi limit" thành 400.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>Cursor rác và <c>limit</c> ngoài <c>1..50</c> đều là 400, không sửa âm thầm.</summary>
public sealed class ListFriendsQueryValidator : AbstractValidator<ListFriendsQuery>
{
    public static readonly string LimitOutOfRange =
        $"Số thẻ mỗi trang phải từ 1 đến {ListFriendsQuery.MaxLimit}.";

    /// <summary>Câu của yaml. Không nói cursor sai chỗ nào: nó opaque.</summary>
    public const string CursorInvalid = "Cursor không hợp lệ.";

    public ListFriendsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ListFriendsQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || FriendCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);
    }
}
