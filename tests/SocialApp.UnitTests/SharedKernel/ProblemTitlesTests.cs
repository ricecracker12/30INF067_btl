using Microsoft.AspNetCore.WebUtilities;
using SocialApp.SharedKernel.Errors;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>Bảng title mặc định (D9): thêm status vào KnownStatuses mà quên nhánh trong For() thì title rơi về tiếng Anh — test bắt.</summary>
public sealed class ProblemTitlesTests
{
    [Fact]
    public void Moi_status_trong_bang_co_title_rieng_khong_phai_reason_phrase_tieng_Anh()
    {
        Assert.All(ProblemTitles.KnownStatuses, status =>
        {
            var title = ProblemTitles.For(status);
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.NotEqual(ReasonPhrases.GetReasonPhrase(status), title);
        });
    }

    [Fact]
    public void Title_theo_hop_dong_va_type_httpstatuses()
    {
        Assert.Equal("Dữ liệu không hợp lệ", ProblemTitles.For(400));
        Assert.Equal("Chưa xác thực", ProblemTitles.For(401));
        Assert.Equal("Quá nhiều yêu cầu", ProblemTitles.For(429));
        Assert.Equal("Đã xảy ra lỗi không mong muốn", ProblemTitles.For(503));
        Assert.Equal("https://httpstatuses.io/423", ProblemTitles.TypeFor(423));
    }

    [Fact]
    public void Status_ngoai_bang_van_co_title()
    {
        Assert.Equal("I'm a teapot", ProblemTitles.For(418));
    }
}
