using FluentValidation;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Application.Media;

/// <summary>
/// Đ-2.8 lớp 1 dưới dạng validator — lớp DUY NHẤT của D4, vì endpoint này không chạm R2 lẫn DB nên không có lớp 2 nào
/// để chạy sau.
///
/// Mọi lỗi về file đều rơi vào MỘT key <c>files</c>, không phải <c>files[2].sizeBytes</c>: hợp đồng ghi
/// <c>errors.files</c> (ví dụ 400 của <c>content-v1.yaml</c>), và FE hiện thông điệp dưới ô chọn ảnh — chỗ đó là một ô,
/// không phải mười ô. Xem <see cref="FileNotAllowed"/> về cái giá đã chấp nhận.
/// </summary>
public sealed class CreateUploadsRequestValidator : AbstractValidator<CreateUploadsRequest>
{
    public const string PurposeRequired = "Mục đích tải lên là bắt buộc.";
    public const string FilesRequired = "Cần ít nhất một file.";

    /// <summary>Số nhân bản từ <see cref="PostContentPolicy.MaxMediaCount"/> — cùng hằng số với BR-01 và <c>ck_posts_media_count</c>.</summary>
    public static readonly string TooManyFiles = $"Tối đa {PostContentPolicy.MaxMediaCount} file một lần.";

    /// <summary>
    /// Chép ĐÚNG câu trong <c>example</c> của <c>content-v1.yaml</c>. Một câu cho cả "loại ngoài allowlist" lẫn "dung
    /// lượng ngoài 1..10 MB", và cố ý KHÔNG nói file thứ mấy sai: cái giá là người dùng phải tự tìm trong mười ảnh, cái
    /// được là <c>errors</c> có đúng key hợp đồng và thông điệp không mang dữ liệu client gửi lên (luật D9).
    ///
    /// Con số MB tính từ <see cref="MediaAttachment.MaxSizeBytes"/> chứ không gõ tay: cùng hằng số mà CHECK
    /// <c>ck_media_size</c> ở DB và <c>MediaHeadPolicy</c> dùng, nên ba chỗ không thể lệch nhau.
    /// </summary>
    public static readonly string FileNotAllowed =
        $"Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa {MediaAttachment.MaxSizeBytes / (1024 * 1024)} MB mỗi ảnh.";

    public CreateUploadsRequestValidator()
    {
        // Q-D2: thiếu `purpose` phải là 400 errors.purpose, không phải "mặc định thành post".
        RuleFor(x => x.Purpose).NotNull().WithMessage(PurposeRequired);

        // Cascade Stop: danh sách rỗng thì hai luật sau không còn gì để nói, và ba câu cùng lúc dưới một ô là ba câu
        // người dùng phải đọc để biết sửa gì. Cũng là thứ giữ `Must` dưới đây khỏi NullReferenceException khi client
        // gửi thẳng `"files": null`.
        RuleFor(x => x.Files)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(FilesRequired)
            .Must(files => files.Count <= PostContentPolicy.MaxMediaCount).WithMessage(TooManyFiles)
            .Must(files => files.All(IsAllowed)).WithMessage(FileNotAllowed);
    }

    /// <summary>
    /// Một file hợp lệ: loại trong allowlist Đ-2.8 và dung lượng khai báo trong <c>1..10485760</c>. <c>0</c> bị từ chối
    /// vì object rỗng không phải ảnh, và vì CHECK <c>ck_media_size</c> ở DB cũng đòi <c>&gt; 0</c> — từ chối ở đây thì
    /// người dùng nhận 400 có câu tiếng Việt, để lọt xuống thì nhận 500 lúc `POST /posts`.
    ///
    /// <c>Must</c> trên cả danh sách chứ không <c>RuleForEach</c>: <c>RuleForEach(...).ChildRules(...)</c> sinh key
    /// <c>files[2].sizeBytes</c>, mà hợp đồng đòi <c>files</c> (kiểm bằng
    /// <c>CreateUploadsRequestValidatorTests.Moi_loi_ve_file_deu_nam_duoi_dung_mot_key_files</c>).
    /// </summary>
    private static bool IsAllowed(UploadFileDeclaration file) =>
        StorageKeys.IsAllowedContentType(file.ContentType)
        && file.SizeBytes is >= 1 and <= MediaAttachment.MaxSizeBytes;
}
