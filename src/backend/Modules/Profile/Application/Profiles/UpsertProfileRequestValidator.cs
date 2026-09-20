using FluentValidation;
using SocialApp.Modules.Profile.Domain;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Khớp ràng buộc của hợp đồng: <c>displayName</c> 2–50 ký tự SAU KHI TRIM, <c>bio</c> ≤ 500 ký tự.
///
/// Hằng số lấy từ <see cref="UserProfile"/>, không gõ lại số: cột DB đọc cùng hai hằng đó
/// (<c>UserProfileConfiguration</c>) nên validator và schema không thể lệch nhau.
/// </summary>
public sealed class UpsertProfileRequestValidator : AbstractValidator<UpsertProfileRequest>
{
    /// <summary>
    /// MỘT câu cho cả "quá ngắn" lẫn "quá dài" lẫn "toàn khoảng trắng". Ba nhánh đó là cùng một điều kiện với người
    /// dùng ("tên phải dài chừng này"), và ba câu khác nhau chỉ làm FE phải nghĩ xem hiện câu nào.
    /// </summary>
    public static readonly string DisplayNameLength =
        $"Tên hiển thị phải có từ {UserProfile.DisplayNameMinLength} đến {UserProfile.DisplayNameMaxLength} ký tự.";

    public static readonly string BioLength = $"Giới thiệu tối đa {UserProfile.BioMaxLength} ký tự.";

    public UpsertProfileRequestValidator()
    {
        // Trim TRƯỚC khi đo — hợp đồng ghi "2–50 ký tự sau khi trim". "   " thành 0 ký tự nên rơi vào cùng nhánh quá
        // ngắn, còn "  An  " là 2 ký tự sau trim nên HỢP LỆ (200, không phải 400).
        //
        // Đây là lần Trim() thứ nhất trong hai lần có chủ đích: validator trim khi ĐO, service trim khi LƯU. Gộp lại
        // không được vì auto-validation chạy trước action, service chưa hề thấy request lúc validator đo.
        RuleFor(x => x.DisplayName)
            .Must(d => d.Trim().Length is >= UserProfile.DisplayNameMinLength and <= UserProfile.DisplayNameMaxLength)
            .WithMessage(DisplayNameLength);

        // KHÔNG trim trước khi đo bio: hợp đồng chỉ ràng buộc độ dài tối đa, và bio là văn bản tự do mà khoảng trắng
        // đầu/cuối không đổi nghĩa. MaximumLength bỏ qua null, nên "không gửi bio" không phải lỗi.
        RuleFor(x => x.Bio).MaximumLength(UserProfile.BioMaxLength).WithMessage(BioLength);
    }
}
