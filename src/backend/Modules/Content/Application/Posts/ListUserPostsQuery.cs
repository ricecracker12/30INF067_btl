using FluentValidation;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Query string của <c>GET /users/{userId}/posts</c>, khớp hai tham số <c>cursor</c> và <c>limit</c> của
/// <c>content-v1.yaml</c>. Là một class <c>[FromQuery]</c> chứ không phải hai tham số rời: nhờ vậy FluentValidation
/// auto-validation áp được (nó chạy trên model phức hợp), và luật "cursor phải giải mã được" có chỗ để sống.
/// </summary>
public sealed class ListUserPostsQuery
{
    /// <summary>Mặc định khi client không gửi <c>limit</c> — hợp đồng ghi <c>default: 20</c>.</summary>
    public const int DefaultLimit = 20;

    /// <summary>Trần cứng, chống một request kéo cả bảng (AGENTS.md Mục 9).</summary>
    public const int MaxLimit = 50;

    /// <summary><c>nextCursor</c> của trang trước; <c>null</c>/vắng mặt = trang đầu.</summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// <c>int?</c> chứ không <c>int</c>: <c>default(int)</c> là <b>0</b>, mà 0 nằm ngoài <c>1..50</c> nên "không gửi
    /// limit" sẽ thành 400 thay vì lấy mặc định 20. Cùng loại bẫy với <c>privacy</c> của Q-D2, chỉ khác là ở đây giá trị
    /// mặc định sai gây khó chịu chứ không gây lộ dữ liệu.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>Số bài thực sự lấy. Gọi SAU khi validator đã chạy.</summary>
    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>
/// Hai luật, và cả hai đều phải là <b>400</b> chứ không phải "âm thầm sửa cho đúng":
/// <list type="bullet">
/// <item><c>limit</c> ngoài <c>1..50</c> → 400. Kẹp về 50 thì client xin 1000 và tưởng mình nhận đủ.</item>
/// <item><c>cursor</c> không giải mã được → 400. Trả trang ĐẦU thì người dùng cuộn mãi không hết (Đ-2.11, PAGE-02).</item>
/// </list>
/// </summary>
public sealed class ListUserPostsQueryValidator : AbstractValidator<ListUserPostsQuery>
{
    public static readonly string LimitOutOfRange =
        $"Số bài mỗi trang phải từ 1 đến {ListUserPostsQuery.MaxLimit}.";

    /// <summary>Câu của yaml. KHÔNG nói cursor sai chỗ nào: nó opaque, người dùng không tự dựng được nó để mà sửa.</summary>
    public const string CursorInvalid = "Cursor không hợp lệ.";

    public ListUserPostsQueryValidator()
    {
        // InclusiveBetween bỏ qua null, nên "không gửi limit" không phải lỗi.
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ListUserPostsQuery.MaxLimit)
            .WithMessage(LimitOutOfRange);

        // Giải mã ở validator chứ không ở service: đây là "dữ liệu đầu vào đúng dạng chưa", đúng việc của tầng này.
        // Service giải mã lại một lần nữa — rẻ (vài chục byte) và giữ service không phụ thuộc vào việc đã qua MVC.
        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || PostCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);
    }
}
