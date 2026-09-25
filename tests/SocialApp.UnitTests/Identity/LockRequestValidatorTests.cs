using SocialApp.Modules.Identity.Application.Admin.Users;

namespace SocialApp.UnitTests.Identity;

/// <summary>GĐ6 D3: <c>reason</c> của <c>POST /admin/users/{userId}/lock</c> dài 1–500 ký tự SAU khi cắt khoảng trắng (Mục 8.2).</summary>
public sealed class LockRequestValidatorTests
{
    private static readonly LockRequestValidator Validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_hoac_rong_la_loi_bat_buoc(string? reason)
    {
        var errors = Validator.Validate(new LockRequest { Reason = reason }).Errors;

        Assert.Equal([LockRequestValidator.ReasonRequired], errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void Qua_500_ky_tu_la_loi_do_dai()
    {
        var errors = Validator.Validate(new LockRequest { Reason = new string('x', 501) }).Errors;

        Assert.Equal([LockRequestValidator.ReasonTooLong], errors.Select(e => e.ErrorMessage));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("  x  ")]
    public void Mot_ky_tu_hop_le(string reason) => Assert.True(Validator.Validate(new LockRequest { Reason = reason }).IsValid);

    [Fact]
    public void Nam_tram_ky_tu_sau_cat_hop_le_khoang_trang_hai_dau_khong_tinh() =>
        Assert.True(Validator.Validate(new LockRequest { Reason = " " + new string('x', 500) + " " }).IsValid);
}
