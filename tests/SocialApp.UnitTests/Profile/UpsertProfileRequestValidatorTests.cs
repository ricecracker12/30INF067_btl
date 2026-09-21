using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.Modules.Profile.Domain;

namespace SocialApp.UnitTests.Profile;

/// <summary>
/// D2 — ranh giới độ dài của <see cref="UpsertProfileRequestValidator"/>. Ở tầng unit vì đây là hàm thuần trên DTO:
/// kiểm bốn giá trị biên ở đây rẻ hơn bốn lượt HTTP, còn integration chỉ cần chứng minh validator THẬT SỰ được nối vào
/// đường request (một case là đủ).
/// </summary>
public sealed class UpsertProfileRequestValidatorTests
{
    private static readonly UpsertProfileRequestValidator Validator = new();

    private static bool IsValid(string displayName, string? bio = null) =>
        Validator.Validate(new UpsertProfileRequest { DisplayName = displayName, Bio = bio }).IsValid;

    /// <summary>
    /// Biên của <c>displayName</c> đo SAU trim. <c>"  An  "</c> là ca quan trọng nhất: 6 ký tự thô nhưng 2 sau trim nên
    /// HỢP LỆ — validator đo trên chuỗi thô thì người dùng gõ thừa một dấu cách bị chặn mà không hiểu vì sao.
    /// </summary>
    [Theory]
    [InlineData("An", true)]                 // đúng bằng tối thiểu
    [InlineData("  An  ", true)]             // 2 ký tự sau trim
    [InlineData("A", false)]                 // ngắn hơn tối thiểu một ký tự
    [InlineData("  A  ", false)]             // 1 ký tự sau trim
    [InlineData("", false)]                  // thiếu trường → chuỗi rỗng (không dùng từ khóa required)
    [InlineData("   ", false)]               // toàn khoảng trắng
    public void Do_dai_ten_hien_thi_do_sau_khi_trim(string displayName, bool expected) =>
        Assert.Equal(expected, IsValid(displayName));

    /// <summary>Hai biên trên của <c>displayName</c>: đúng 50 qua, 51 không.</summary>
    [Fact]
    public void Ten_hien_thi_toi_da_dung_bang_hang_so_cua_entity()
    {
        Assert.True(IsValid(new string('a', UserProfile.DisplayNameMaxLength)));
        Assert.False(IsValid(new string('a', UserProfile.DisplayNameMaxLength + 1)));
    }

    /// <summary>
    /// <c>bio</c> không bắt buộc, tối đa <see cref="UserProfile.BioMaxLength"/>. Chuỗi rỗng qua validator — service mới
    /// là chỗ đổi nó thành <c>null</c> (Q-D3), không phải validator: "rỗng" là dữ liệu hợp lệ nghĩa là "xóa bio".
    /// </summary>
    [Fact]
    public void Bio_khong_bat_buoc_va_toi_da_dung_bang_hang_so_cua_entity()
    {
        Assert.True(IsValid("An", bio: null));
        Assert.True(IsValid("An", bio: ""));
        Assert.True(IsValid("An", bio: new string('b', UserProfile.BioMaxLength)));
        Assert.False(IsValid("An", bio: new string('b', UserProfile.BioMaxLength + 1)));
    }

    /// <summary>
    /// Thông điệp phải nêu đúng hai con số của hợp đồng. Khẳng định trên hằng số chứ không so nguyên câu: đổi cách diễn
    /// đạt là việc của người viết, đổi con số là đổi hợp đồng.
    /// </summary>
    [Fact]
    public void Thong_diep_neu_dung_hai_con_so_cua_hop_dong()
    {
        Assert.Contains($"{UserProfile.DisplayNameMinLength}", UpsertProfileRequestValidator.DisplayNameLength, StringComparison.Ordinal);
        Assert.Contains($"{UserProfile.DisplayNameMaxLength}", UpsertProfileRequestValidator.DisplayNameLength, StringComparison.Ordinal);
        Assert.Contains($"{UserProfile.BioMaxLength}", UpsertProfileRequestValidator.BioLength, StringComparison.Ordinal);
    }
}
