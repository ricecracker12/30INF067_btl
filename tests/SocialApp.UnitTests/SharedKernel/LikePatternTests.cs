using SocialApp.SharedKernel.Text;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// GĐ6 D2: mẫu <c>LIKE</c> dùng chung cho <c>GET /admin/users?q=</c> (D2) và <c>GET /search</c> (D12). Kỳ vọng viết tay, không
/// dựng lại bằng chính <c>Replace</c> của hàm được kiểm.
/// </summary>
public sealed class LikePatternTests
{
    [Theory]
    [InlineData("an", "an")]
    [InlineData("a%", @"a\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"a\b", @"a\\b")]
    [InlineData(@"\%", @"\\\%")]   // \ thoát TRƯỚC: thoát sau thì \ vừa chèn cho % bị nhân đôi và % lại thành đại diện
    [InlineData("%_%", @"\%\_\%")]
    [InlineData("", "")]
    public void Escape_hieu_ky_tu_dac_biet_theo_nghia_den(string input, string expected) =>
        Assert.Equal(expected, LikePattern.Escape(input));

    [Fact]
    public void StartsWith_them_dung_mot_dai_dien_o_cuoi()
    {
        Assert.Equal(@"a\_%", LikePattern.StartsWith("a_"));
        Assert.Equal("%", LikePattern.StartsWith(""));
    }

    [Fact]
    public void Ky_tu_thoat_la_gach_cheo_nguoc() => Assert.Equal(@"\", LikePattern.EscapeCharacter);
}
