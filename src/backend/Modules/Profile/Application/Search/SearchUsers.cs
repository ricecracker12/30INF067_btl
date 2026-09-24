using FluentValidation;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;
using SocialApp.SharedKernel.Text;

namespace SocialApp.Modules.Profile.Application.Search;

/// <summary>
/// Query string của <c>GET /search</c> (<c>profile-v1.yaml</c>, Đ-6.19). Class <c>[FromQuery]</c> để FluentValidation auto-validation chạy.
/// </summary>
public sealed class SearchUsersQuery
{
    public const int MinTermLength = 2;

    public const int MaxTermLength = 50;

    public const int DefaultLimit = 10;

    public const int MaxLimit = 20;

    /// <summary>Loại duy nhất hiện có. Tham số tồn tại để thêm loại sau (bài viết…) là chỉ-thêm, không đổi hợp đồng.</summary>
    public const string UserType = "user";

    /// <summary>Chuỗi người dùng gõ. Đo và tìm trên bản ĐÃ <c>Trim()</c>.</summary>
    public string? Q { get; init; }

    /// <summary>Vắng mặt = <see cref="UserType"/>.</summary>
    public string? Type { get; init; }

    /// <summary><c>int?</c>: <c>default(int)</c> là 0, nằm ngoài <c>1..20</c>, nên "không gửi limit" thành 400.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;

    public string Term => Q?.Trim() ?? "";
}

/// <summary>Sai dạng → 400 theo trường. Không sửa âm thầm (không cắt <c>q</c> dài, không kẹp <c>limit</c>).</summary>
public sealed class SearchUsersQueryValidator : AbstractValidator<SearchUsersQuery>
{
    public const string TermTooShort = "Nhập ít nhất 2 ký tự.";

    public const string TermTooLong = "Tối đa 50 ký tự.";

    public const string TypeInvalid = "Chỉ hỗ trợ tìm người dùng (type=user).";

    public static readonly string LimitOutOfRange = $"Số kết quả phải từ 1 đến {SearchUsersQuery.MaxLimit}.";

    public SearchUsersQueryValidator()
    {
        // Thiếu q, q rỗng, q chỉ khoảng trắng: cùng một câu "ít nhất 2 ký tự" dưới cùng key `q`.
        RuleFor(x => x.Term)
            .Must(t => t.Length >= SearchUsersQuery.MinTermLength).WithMessage(TermTooShort)
            .Must(t => t.Length <= SearchUsersQuery.MaxTermLength).WithMessage(TermTooLong)
            .OverridePropertyName("q");

        RuleFor(x => x.Type)
            .Must(t => t is null || t == SearchUsersQuery.UserType)
            .WithMessage(TypeInvalid);

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, SearchUsersQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);
    }
}

/// <param name="AvatarUrl">Presigned GET, <c>null</c> khi chưa có ảnh đại diện.</param>
public sealed record SearchResult(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>Không cursor (Đ-6.19): tối đa 20 kết quả tốt nhất là đủ cho ô gõ tìm; ai cần hơn thì gõ thêm chữ.</summary>
public sealed record SearchPage(IReadOnlyList<SearchResult> Items);

/// <summary>Một hồ sơ khớp, như DB trả, đã xếp hạng.</summary>
public sealed record SearchHit(Guid UserId, string DisplayName, string? AvatarKey);

/// <summary>
/// Truy vấn tìm tên của Profile (Đ-6.19). Hiện thực chuẩn hóa CẢ HAI vế bằng đúng <c>profile.search_norm</c> — biểu thức của index GIN A4;
/// viết <c>lower(unaccent(…))</c> là biểu thức khác, planner bỏ index (cạm bẫy 1).
/// </summary>
public interface IProfileSearch
{
    /// <param name="escapedTerm"><see cref="LikePattern.Escape"/> của từ khóa — ký tự đại diện hiểu theo nghĩa đen.</param>
    /// <param name="rawTerm">Từ khóa chưa escape — chỉ để xếp hạng bằng <c>similarity</c>.</param>
    /// <param name="take">Người gọi lấy dư để còn đủ sau khi lọc tài khoản không hoạt động.</param>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string escapedTerm, string rawTerm, int take, CancellationToken ct);
}

/// <summary>
/// <c>GET /search?q=&amp;type=user&amp;limit=</c> (GĐ6 D12, FR-017, UC-16). Hai câu SQL mỗi lượt, không phụ thuộc số kết quả: một câu tìm + một
/// lô <see cref="IAccountStatusReader"/>. Không trả trạng thái quan hệ — <c>IFriendshipReader</c> chỉ có bản đơn, tức N+1 ở ô gõ phím
/// (cạm bẫy 5).
/// </summary>
public sealed class SearchService(IProfileSearch search, IAccountStatusReader accounts, IObjectStorage storage)
{
    /// <summary>
    /// Lấy dư <see cref="Overfetch"/> dòng rồi lọc tài khoản không hoạt động (bị khóa, đã xóa) bằng C5 — lọc bằng join sang
    /// <c>identity.users</c> là đọc chéo schema (cạm bẫy 4). Nhiều hơn chừng đó người bị khóa lọt vào top thì trang trả ít hơn
    /// <c>limit</c> — chấp nhận.
    /// </summary>
    public const int Overfetch = 5;

    /// <summary>Gọi SAU validator.</summary>
    public async Task<SearchPage> SearchAsync(SearchUsersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Term;
        var limit = query.EffectiveLimit;
        var hits = await search.SearchAsync(LikePattern.Escape(term), term, limit + Overfetch, ct);
        if (hits.Count == 0)
            return new SearchPage([]);

        var inactive = await accounts.GetInactiveAsync([.. hits.Select(h => h.UserId)], ct);

        return new SearchPage([.. hits
            .Where(h => !inactive.Contains(h.UserId))
            .Take(limit)
            .Select(h => new SearchResult(
                h.UserId, h.DisplayName, h.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null))]);
    }
}
