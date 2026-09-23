using SocialApp.Modules.Identity.Domain;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Canh <see cref="PermissionCodes.Descriptions"/> (GĐ6 A3): seeder tra <c>Descriptions[code]</c> cho MỌI mã trong
/// <see cref="PermissionCodes.All"/> — thiếu một mã là <c>KeyNotFoundException</c> ở <c>--migrate</c>, tức deploy đỏ.
/// </summary>
public sealed class PermissionCodesTests
{
    [Fact]
    public void Moi_ma_trong_All_deu_co_mo_ta_va_khong_thua()
    {
        Assert.Equal(
            PermissionCodes.All.OrderBy(c => c, StringComparer.Ordinal),
            PermissionCodes.Descriptions.Keys.OrderBy(c => c, StringComparer.Ordinal));
    }

    /// <summary>Cột <c>permissions.description</c> là <c>varchar(120)</c>; chuỗi rỗng thì màn ma trận quyền hiện ô trống.</summary>
    [Fact]
    public void Mo_ta_vua_cot_va_khong_rong()
    {
        Assert.All(PermissionCodes.Descriptions.Values, d => Assert.InRange(d.Trim().Length, 1, 120));
    }

    /// <summary>
    /// role.manage đứng CUỐI (id 18): id = vị trí + 1, chèn giữa là đổi id của mọi mã sau nó trên DB đã seed.
    /// </summary>
    [Fact]
    public void Role_manage_la_ma_18_o_cuoi()
    {
        Assert.Equal(18, PermissionCodes.All.Length);
        Assert.Equal("role.manage", PermissionCodes.All[^1]);
    }
}
