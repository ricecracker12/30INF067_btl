using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Content.Application.Media;

/// <summary>
/// Body của <c>POST /media/uploads</c>, khớp schema <c>CreateUploadsRequest</c> của <c>content-v1.yaml</c>. Một request
/// cho tối đa 10 file (Đ-2.15): bài 10 ảnh tốn MỘT lượt presign, không phải mười — hạn mức chung là 100 req/phút/user.
/// </summary>
public sealed class CreateUploadsRequest
{
    /// <summary>
    /// <b>Nullable có chủ đích</b> (Q-D2). <c>UploadPurpose Purpose</c> không nullable thì body thiếu <c>purpose</c> rơi
    /// vào <c>default(UploadPurpose)</c> = <see cref="UploadPurpose.Post"/> — tức là mặc định thành mức quyền CAO hơn,
    /// đúng loại hỏng im lặng mà Q-D2 chốt phải tránh. Nullable + <c>NotNull()</c> ở validator thì thiếu trường là 400
    /// <c>errors.purpose</c>, không phải một suy đoán.
    ///
    /// <c>[Required]</c> ở đây chỉ để Swagger ghi <c>required: [purpose, files]</c> — DataAnnotations đã tắt validate
    /// (<c>Program.cs</c>), người bắt lỗi thật là validator.
    /// </summary>
    [Required]
    public UploadPurpose? Purpose { get; init; }

    /// <summary>
    /// Mặc định danh sách RỖNG, không <c>null</c>: <c>files</c> vắng mặt và <c>files: []</c> là cùng một ý với người
    /// dùng ("không khai file nào") và phải ra cùng một câu lỗi. Để <c>null</c> thì validator phải mang thêm một nhánh
    /// chỉ để phân biệt hai thứ không phân biệt được ở tầng nghiệp vụ.
    /// </summary>
    [Required]
    public List<UploadFileDeclaration> Files { get; init; } = [];
}
