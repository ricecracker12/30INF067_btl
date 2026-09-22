using SocialApp.Modules.Identity.Domain;
using SocialApp.Modules.SocialGraph.Application;
using Xunit;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// L6 / nếp Q-D5 (D0): <see cref="SocialGraphPermissions"/> là bản sao CHUỖI của hai mã quyền mà module SocialGraph
/// dùng — SocialGraph không được <c>using</c> <see cref="PermissionCodes"/> của Identity (luật 4, <see cref="ModuleBoundaryTests"/>
/// canh). Cái giá của bản sao là nó lệch được trong im lặng, và lệch theo kiểu tệ nhất:
/// <c>[RequirePermission("friend.reques")]</c> vẫn compile, Admin vẫn qua (short-circuit của <c>PermissionHandler</c>
/// không nhìn mã quyền), nên người kiểm tay bằng tài khoản Admin thấy mọi thứ chạy tốt trong khi USER bị chặn vĩnh viễn.
///
/// Project này tham chiếu <c>SocialApp.Api</c> nên thấy được CẢ HAI module — đây là chỗ duy nhất so được hai bên mà không
/// phá ranh giới module ở code sản phẩm.
///
/// Đã thử cho đỏ một lần: đổi tạm <c>FriendRequest</c> thành <c>"friend.reques"</c> → đỏ đúng mã đó → khôi phục.
/// </summary>
public sealed class SocialGraphPermissionsTests
{
    [Fact]
    public void Moi_ma_cua_SocialGraphPermissions_deu_co_trong_PermissionCodes()
    {
        var known = PermissionCodes.All.ToHashSet(StringComparer.Ordinal);

        var unknown = SocialGraphPermissions.All.Where(code => !known.Contains(code)).ToList();

        Assert.True(unknown.Count == 0,
            "SocialGraphPermissions khai mã quyền không có trong PermissionCodes của Identity (gõ sai? Admin vẫn qua nên "
          + "kiểm tay bằng Admin không thấy): " + string.Join(", ", unknown));
    }

    /// <summary>
    /// Canh gác chống "lưới giả", cùng bài học với <see cref="ContentPermissionsTests.All_liet_ke_du_moi_hang_cua_ContentPermissions"/>:
    /// <c>SocialGraphPermissions.All</c> bỏ sót một hằng thì rule trên xanh vĩnh viễn với chính cái hằng bị bỏ sót.
    /// So số phần tử của mảng với số hằng <c>public const string</c> đọc bằng reflection — thêm hằng mà quên thêm vào
    /// <c>All</c> là đỏ ngay.
    /// </summary>
    [Fact]
    public void All_liet_ke_du_moi_hang_cua_SocialGraphPermissions()
    {
        var constants = typeof(SocialGraphPermissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(constants.Count, SocialGraphPermissions.All.Length);
        Assert.Empty(constants.Except(SocialGraphPermissions.All, StringComparer.Ordinal));
    }
}
