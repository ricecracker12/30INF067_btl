using SocialApp.Modules.Identity.Application.Roles;

namespace SocialApp.UnitTests.Identity;

/// <summary>GĐ6 D5: <see cref="RolePermissionDiff"/> (hàm thuần) và ba validator của CRUD vai trò, gồm L-D19 (<c>role.manage</c> không gán được).</summary>
public sealed class RoleRulesTests
{
    private static HashSet<string> Set(params string[] codes) => new(codes, StringComparer.Ordinal);

    [Fact]
    public void Diff_them_va_bot_sap_Ordinal()
    {
        var diff = RolePermissionDiff.Compute(Set("post.create", "comment.create"), Set("reaction.set", "post.create", "friend.request"));

        Assert.Equal(["friend.request", "reaction.set"], diff.Added);
        Assert.Equal(["comment.create"], diff.Removed);
        Assert.True(diff.HasChanges);
        Assert.False(diff.IsEmptyAfter);
    }

    [Fact]
    public void Diff_khong_doi_gi_khong_co_thay_doi()
    {
        var diff = RolePermissionDiff.Compute(Set("a.b", "c.d"), Set("c.d", "a.b"));

        Assert.False(diff.HasChanges);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Diff_ve_rong_la_IsEmptyAfter_va_bo_het()
    {
        var diff = RolePermissionDiff.Compute(Set("b.x", "a.x"), Set());

        Assert.True(diff.IsEmptyAfter);
        Assert.Equal(["a.x", "b.x"], diff.Removed);
    }

    [Fact]
    public void Diff_phan_biet_hoa_thuong()
    {
        var diff = RolePermissionDiff.Compute(Set("post.create"), Set("POST.CREATE"));

        Assert.Equal(["POST.CREATE"], diff.Added);
        Assert.Equal(["post.create"], diff.Removed);
    }

    [Fact]
    public void Role_manage_khong_gan_duoc_cac_ma_khac_thi_duoc()
    {
        Assert.DoesNotContain("role.manage", RoleRules.AssignablePermissions);
        Assert.Equal(17, RoleRules.AssignablePermissions.Count);
        Assert.Contains("post.create", RoleRules.AssignablePermissions);
    }

    [Theory]
    [InlineData("REVIEWER", true)]
    [InlineData("R2_D2", true)]
    [InlineData("ABC", true)]
    [InlineData("AB", false)]
    [InlineData("reviewer", false)]
    [InlineData("2FA", false)]
    [InlineData("_ABC", false)]
    [InlineData("A-B-C", false)]
    [InlineData("REVIEWER\n", false)]   // "$" của .NET khớp trước "\n" cuối chuỗi — so từng ký tự thì không lọt
    public void Hinh_dang_ma_vai_tro(string code, bool ok) => Assert.Equal(ok, RoleRules.IsRoleCodeShape(code));

    [Theory]
    [InlineData("USER")]
    [InlineData("MODERATOR")]
    [InlineData("ADMIN")]
    public void Tao_vai_tro_trung_ma_he_thong_loi_code(string code)
    {
        var errors = new CreateRoleRequestValidator()
            .Validate(new CreateRoleRequest { Code = code, DisplayName = "x", Permissions = [] }).Errors;

        Assert.Equal([CreateRoleRequestValidator.CodeIsSystem], errors.Select(e => e.ErrorMessage));
    }

    [Theory]
    [InlineData("role.manage")]
    [InlineData("khong.co")]
    [InlineData("POST.CREATE")]
    public void Tao_vai_tro_ma_quyen_khong_gan_duoc_loi_permissions(string permission)
    {
        var errors = new CreateRoleRequestValidator()
            .Validate(new CreateRoleRequest { Code = "REVIEWER", DisplayName = "x", Permissions = [permission] }).Errors;

        Assert.Equal(["Permissions"], errors.Select(e => e.PropertyName));
    }

    [Fact]
    public void Tao_vai_tro_hop_le_ke_ca_tap_quyen_rong() =>
        Assert.True(new CreateRoleRequestValidator()
            .Validate(new CreateRoleRequest { Code = "REVIEWER", DisplayName = " Người xem ", Permissions = [] }).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]   // 51
    public void Ten_hien_thi_sai_loi(string? name) =>
        Assert.False(new RenameRoleRequestValidator().Validate(new RenameRoleRequest { DisplayName = name }).IsValid);

    [Fact]
    public void Sua_tap_quyen_thieu_permissions_hoac_co_role_manage_loi()
    {
        var validator = new SetRolePermissionsRequestValidator();

        Assert.False(validator.Validate(new SetRolePermissionsRequest { Permissions = null }).IsValid);
        Assert.False(validator.Validate(new SetRolePermissionsRequest { Permissions = ["role.manage"] }).IsValid);
        Assert.True(validator.Validate(new SetRolePermissionsRequest { Permissions = [] }).IsValid);   // "về 0" là luật của store
    }
}
