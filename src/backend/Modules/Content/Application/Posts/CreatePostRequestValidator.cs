using System.Text.RegularExpressions;
using FluentValidation;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Lớp "không cần I/O" của <c>POST /posts</c>: BR-01 tĩnh, dạng key, và khai báo từng ảnh. Ba lớp của Đ-2.8 chạy sau ở
/// service (tiền tố → 403) và ở <see cref="MediaHeadPolicy"/> (HEAD → 400).
///
/// Mọi lỗi về ảnh nằm dưới MỘT key <c>mediaKeys</c>, mọi lỗi về chữ dưới <c>body</c> — đúng hai key hợp đồng ghi, và
/// đúng hai ô người dùng đang đứng.
/// </summary>
public sealed class CreatePostRequestValidator : AbstractValidator<CreatePostRequest>
{
    public const string PrivacyRequired = "Mức riêng tư là bắt buộc.";

    /// <summary>
    /// Chép NGUYÊN <c>MediaKeyDeclaration.mediaKey.pattern</c> của <c>content-v1.yaml</c> — hợp đồng là nguồn sự thật.
    /// <c>[0-9a-f-]{36}</c> là Guid dạng "D" viết thường, <c>[0-9a-f]{32}</c> là UUID v7 dạng "N" mà
    /// <see cref="StorageKeys.ForPost"/> sinh ra.
    ///
    /// Regex neo HAI ĐẦU (<c>^</c> và <c>$</c>) — cùng bài học với <c>SetAvatarRequestValidator</c> của D3: không neo thì
    /// <c>"rác/" + key</c> lọt cả lớp này lẫn lớp tiền tố (<see cref="StorageKeys.BelongsTo"/> dùng <c>StartsWith</c>,
    /// chỉ nhìn phần đầu).
    /// </summary>
    public const string MediaKeyPattern = @"^posts/[0-9a-f-]{36}/[0-9a-f]{32}\.(jpg|png|webp)$";

    /// <summary>Một câu cho mọi cách sai dạng — cùng câu với D3, để một loại lỗi chỉ có một cách nói.</summary>
    public const string MediaKeyInvalid = "Khóa ảnh không đúng dạng.";

    /// <summary>
    /// Khai báo loại/dung lượng ngoài allowlist. Cùng CHÍNH XÁC câu của <c>POST /media/uploads</c> (D4): người dùng vừa
    /// xin URL với câu đó ở bước 1, nhận một câu khác ở bước 3 cho cùng một luật là tự mâu thuẫn.
    /// </summary>
    public static readonly string MediaDeclarationInvalid =
        $"Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa {MediaAttachment.MaxSizeBytes / (1024 * 1024)} MB mỗi ảnh.";

    private static readonly Regex MediaKeyRegex =
        new(MediaKeyPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public CreatePostRequestValidator()
    {
        // Q-D2. Xem CreatePostRequest.Privacy về cái giá của việc bỏ dấu `?`.
        RuleFor(x => x.Privacy).NotNull().WithMessage(PrivacyRequired);

        // BR-01 qua HÀM THUẦN của Domain, không gõ lại luật ở đây: một nguồn cho thông điệp VÀ cho key
        // (AC-02 → `body`, AC-03 → `mediaKeys`). `RuleFor(x => x)` có PropertyChain rỗng nên `AddFailure(key, ...)`
        // đặt đúng key mà PostContentPolicy chọn, không bị ghép thêm tiền tố nào.
        RuleFor(x => x).Custom((request, ctx) =>
        {
            var br01 = PostContentPolicy.Validate(request.Body, request.MediaKeys?.Count ?? 0);
            if (!br01.IsValid)
                ctx.AddFailure(br01.ErrorKey!, br01.Message!);
        });

        // Chỉ soi từng phần tử khi SỐ LƯỢNG đã hợp lệ: quá 10 ảnh thì mệnh đề BR-01 ở trên đã nói rồi, và hai câu cùng
        // lúc dưới một ô là hai câu người dùng phải đọc để biết sửa gì.
        When(HasCheckableMedia, () =>
            RuleFor(x => x.MediaKeys)
                .Cascade(CascadeMode.Stop)
                .Must(AllWellFormed).WithMessage(MediaKeyInvalid)
                .Must(NoDuplicates).WithMessage(ContentErrors.DuplicateMediaKeysMessage)
                .Must(AllDeclarationsAllowed).WithMessage(MediaDeclarationInvalid));
    }

    private static bool HasCheckableMedia(CreatePostRequest request) =>
        request.MediaKeys is { Count: > 0 } media && media.Count <= PostContentPolicy.MaxMediaCount;

    /// <summary>
    /// Dạng key TRƯỚC trùng lặp: hai key rác giống hệt nhau mà báo "một ảnh không được đính kèm hai lần" là trả lời sai
    /// câu hỏi.
    /// </summary>
    private static bool AllWellFormed(List<MediaKeyDeclaration>? media) =>
        media!.TrueForAll(m => m.MediaKey is not null && MediaKeyRegex.IsMatch(m.MediaKey));

    /// <summary>
    /// Bắt ở đây chứ không để chạm DB: <c>storage_key</c> có UNIQUE, nên hai key trùng trong CÙNG một request sẽ nổ
    /// unique violation và người dùng nhận <b>409 "ảnh đã dùng ở bài khác"</b> — sai hẳn nguyên nhân, vì bài khác đó
    /// chính là bài họ đang gửi.
    /// </summary>
    private static bool NoDuplicates(List<MediaKeyDeclaration>? media) =>
        media!.Select(m => m.MediaKey).Distinct(StringComparer.Ordinal).Count() == media!.Count;

    private static bool AllDeclarationsAllowed(List<MediaKeyDeclaration>? media) =>
        media!.TrueForAll(m =>
            StorageKeys.IsAllowedContentType(m.ContentType)
            && m.SizeBytes is >= 1 and <= MediaAttachment.MaxSizeBytes);
}
