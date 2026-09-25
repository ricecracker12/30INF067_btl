using SocialApp.Modules.Identity.Application.Roles;
using SocialApp.Modules.Identity.Domain;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Quyền hiệu lực để hiển thị (Đ-6.11). ADMIN không có dòng role_permissions nào — đọc bảng thì rỗng — nên hàm phải trả đủ 18 mã;
/// vai trò khác trả đúng thứ bảng có, không thêm không bớt.
/// </summary>
public sealed class EffectivePermissionsTests
{
    [Fact]
    public void ADMIN_nhan_du_moi_ma_theo_thu_tu_PermissionCodes_All_du_bang_rong()
    {
        var result = EffectivePermissions.For(RoleCodes.Admin, []);

        Assert.Equal(PermissionCodes.All, result);
        Assert.Contains(PermissionCodes.RoleManage, result);
    }

    [Theory]
    [InlineData("USER")]
    [InlineData("MODERATOR")]
    [InlineData("REVIEWER")]
    public void Vai_tro_khac_nhan_dung_tap_cua_bang(string roleCode)
    {
        string[] granted = [PermissionCodes.PostCreate, PermissionCodes.ReportResolve];

        Assert.Equal(granted, EffectivePermissions.For(roleCode, granted));
    }

    /// <summary>So chính xác: "admin" chữ thường là một vai trò tự tạo bình thường, không phải lối tắt (khớp PermissionHandlerTests).</summary>
    [Fact]
    public void Admin_chu_thuong_khong_di_loi_tat()
    {
        Assert.Empty(EffectivePermissions.For("admin", []));
    }
}
