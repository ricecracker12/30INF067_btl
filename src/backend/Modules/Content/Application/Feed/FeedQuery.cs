using FluentValidation;
using SocialApp.Modules.Content.Application.Posts;

namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Query string của <c>GET /feed</c> — chép <see cref="ListUserPostsQuery"/>: class <c>[FromQuery]</c> để FluentValidation
/// auto-validation áp được, <c>int?</c> để "không gửi limit" là mặc định chứ không phải 0 → 400.
/// </summary>
public sealed class FeedQuery
{
    /// <summary><c>nextCursor</c> của trang trước; <c>null</c>/vắng mặt = trang đầu (trang duy nhất được cache — Đ-4.8).</summary>
    public string? Cursor { get; init; }

    public int? Limit { get; init; }

    /// <summary>Số bài thực sự lấy. Gọi SAU khi validator đã chạy.</summary>
    public int EffectiveLimit => Limit ?? FeedService.DefaultLimit;
}

/// <summary>
/// Cùng hai luật và CÙNG câu với <see cref="ListUserPostsQueryValidator"/> — một loại lỗi, một cách nói: <c>limit</c> ngoài
/// <c>1..50</c> → 400 <c>errors.limit</c>; cursor không giải mã được → 400 <c>errors.cursor</c> (<c>PAGE-04</c>), không âm
/// thầm trả trang đầu.
/// </summary>
public sealed class FeedQueryValidator : AbstractValidator<FeedQuery>
{
    public FeedQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, FeedService.MaxLimit)
            .WithMessage(ListUserPostsQueryValidator.LimitOutOfRange);

        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || PostCursor.TryDecode(raw, out _))
            .WithMessage(ListUserPostsQueryValidator.CursorInvalid);
    }
}
