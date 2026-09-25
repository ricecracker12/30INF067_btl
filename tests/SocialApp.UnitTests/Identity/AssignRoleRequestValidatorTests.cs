using SocialApp.Modules.Identity.Application.Admin.Users;

namespace SocialApp.UnitTests.Identity;

/// <summary>GĐ6 D4: <c>roleCode</c> của <c>PUT /admin/users/{userId}/role</c> không rỗng, tối đa 30 ký tự. Tồn tại hay không là việc của store.</summary>
public sealed class AssignRoleRequestValidatorTests
{
    private static readonly AssignRoleRequestValidator Validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Thieu_hoac_rong_la_loi_bat_buoc(string? code) =>
        Assert.Equal([AssignRoleRequestValidator.RoleCodeRequired],
            Validator.Validate(new AssignRoleRequest { RoleCode = code }).Errors.Select(e => e.ErrorMessage));

    [Fact]
    public void Qua_30_ky_tu_la_loi_do_dai() =>
        Assert.Equal([AssignRoleRequestValidator.RoleCodeTooLong],
            Validator.Validate(new AssignRoleRequest { RoleCode = new string('A', 31) }).Errors.Select(e => e.ErrorMessage));

    [Theory]
    [InlineData("USER")]
    [InlineData("user")]   // sai hoa thường KHÔNG là lỗi validator — store trả "Vai trò không tồn tại."
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Dung_do_dai_hop_le(string code) =>
        Assert.True(Validator.Validate(new AssignRoleRequest { RoleCode = code }).IsValid);
}
