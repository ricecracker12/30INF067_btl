using FluentValidation;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Body của <c>PATCH /posts/{postId}</c>, khớp schema <c>UpdatePostRequest</c> của <c>content-v1.yaml</c>. Hai trường,
/// cả hai tùy chọn — nhưng gửi <b>không trường nào</b> là 400 (xem <see cref="UpdatePostRequestValidator"/>).
///
/// <b>Không có <c>MediaKeys</c></b>, và đó là cả cơ chế chặn: GĐ2 không cho sửa ảnh (Mục 7.3), nên client gửi
/// <c>mediaKeys</c> vào đây là <b>field lạ</b> → 400 nhờ <c>UnmappedMemberHandling.Disallow</c> ở <c>Program.cs</c>.
/// Thêm một luật validator cho <c>mediaKeys</c> là thừa, và tệ hơn: nó gợi ý rằng trường đó có tồn tại.
/// </summary>
public sealed class UpdatePostRequest
{
    /// <summary>
    /// Nội dung mới. <b><c>null</c> ở đây nghĩa là "không gửi"</b>, không phải "xóa chữ" — System.Text.Json không phân
    /// biệt vắng mặt với <c>null</c> cho <c>string?</c> (cùng lý do đã chốt ở Q-D3 cho <c>bio</c>).
    ///
    /// Muốn xóa chữ thì gửi <c>""</c>, và khi đó BR-01 quyết định: bài có ảnh → được (<c>body</c> thành <c>null</c>),
    /// bài không ảnh → 400 <c>errors.body</c>.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Mức riêng tư mới; <c>null</c> = không đổi. Nullable ở đây là tùy chọn THẬT (khác
    /// <see cref="CreatePostRequest.Privacy"/>, nơi nullable tồn tại để chặn mặc định ngầm thành <c>public</c>).
    /// </summary>
    public PostPrivacy? Privacy { get; init; }
}

/// <summary>
/// Hai luật, và luật thứ nhất là thứ duy nhất phân biệt "sửa không có gì" với "sửa thành rỗng":
/// <list type="bullet">
/// <item>Body <c>{}</c> → 400 <c>errors.body</c> "Không có gì để sửa." Không chặn thì request rỗng đi hết đường xuống
/// store, đóng dấu <c>edited_at</c> và <c>updated_at</c> cho một thay đổi không tồn tại.</item>
/// <item><c>body</c> quá dài → 400. Mệnh đề BR-01 còn lại (rỗng thì phải có ảnh) cần <c>media_count</c> THẬT của bài nên
/// chỉ kiểm được ở service, sau khi đọc dòng.</item>
/// </list>
/// </summary>
public sealed class UpdatePostRequestValidator : AbstractValidator<UpdatePostRequest>
{
    public UpdatePostRequestValidator()
    {
        // WithName("body") vì RuleFor(x => x) có tên rỗng: key phải là một Ô người dùng đang đứng, và ô đó là ô soạn
        // chữ — cùng lập luận với PostContentPolicy.Empty (AC-02). Câu lấy từ ContentErrors để service và validator
        // không nói hai kiểu.
        RuleFor(x => x)
            .Must(r => r.Body is not null || r.Privacy is not null)
            .WithName(PostContentPolicy.BodyKey)
            .WithMessage(ContentErrors.NothingToUpdateMessage);

        RuleFor(x => x.Body)
            .MaximumLength(PostContentPolicy.MaxBodyLength)
            .WithMessage(PostContentPolicy.BodyTooLong);
    }
}
