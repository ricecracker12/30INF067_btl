using FluentValidation;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Query string của <c>GET /friends/requests</c>. <see cref="Direction"/> là <c>string?</c>, không phải enum:
/// bind thẳng enum thì giá trị lạ ra thông điệp tiếng Anh của MVC, không phải <c>errors.direction</c>.
/// </summary>
public sealed class ListFriendRequestsQuery
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 50;

    public const string Incoming = "incoming";

    public const string Outgoing = "outgoing";

    public string? Cursor { get; init; }

    public int? Limit { get; init; }

    /// <summary><c>null</c> khi client không gửi — mặc định <see cref="Incoming"/> ở controller, sau khi validator chạy.</summary>
    public string? Direction { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>Thêm luật <c>direction</c> vào hai luật cursor/limit của danh sách bạn.</summary>
public sealed class ListFriendRequestsQueryValidator : AbstractValidator<ListFriendRequestsQuery>
{
    public static readonly string LimitOutOfRange =
        $"Số thẻ mỗi trang phải từ 1 đến {ListFriendRequestsQuery.MaxLimit}.";

    public const string CursorInvalid = "Cursor không hợp lệ.";

    public const string DirectionInvalid = "Chiều lời mời phải là incoming hoặc outgoing.";

    public ListFriendRequestsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ListFriendRequestsQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || FriendCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);

        RuleFor(x => x.Direction)
            .Must(raw => raw is null
                         or ListFriendRequestsQuery.Incoming
                         or ListFriendRequestsQuery.Outgoing)
            .WithMessage(DirectionInvalid);
    }
}
