using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Identity.Domain;
using Xunit;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Q-D5 (D0): <see cref="ContentPermissions"/> là bản sao CHUỖI của bốn mã quyền mà module Content dùng — Content không được
/// <c>using</c> <see cref="PermissionCodes"/> của Identity (luật 4 của khối D, <see cref="ModuleBoundaryTests"/> canh).
/// Cái giá của bản sao là nó lệch được trong im lặng, và lệch theo kiểu tệ nhất:
/// <c>[RequirePermission("post.creat")]</c> vẫn compile, Admin vẫn qua (short-circuit của <c>PermissionHandler</c> không
/// nhìn mã quyền), nên người kiểm tay bằng tài khoản Admin thấy mọi thứ chạy tốt trong khi USER bị chặn vĩnh viễn.
///
/// Project này tham chiếu <c>SocialApp.Api</c> nên thấy được CẢ HAI module — đây là chỗ duy nhất so được hai bên mà không
/// phá ranh giới module ở code sản phẩm.
///
/// Khác <see cref="PermissionCodeUsageTests"/> ở chỗ nào: lớp đó đọc giá trị trong <c>[RequirePermission]</c> đã gắn lên
/// action, nên nó chỉ canh được mã ĐÃ ĐƯỢC DÙNG qua attribute. Mã của Q-D5 đi qua <c>IAuthorizationService</c> ở thân
/// action (<c>purpose=post</c> của D4) — không có attribute nào để đọc, nên cần lớp này.
///
/// Đã thử cho đỏ một lần: đổi tạm <c>PostCreate</c> thành <c>"post.creat"</c> → đỏ đúng mã đó → khôi phục.
/// </summary>
public sealed class ContentPermissionsTests
{
    [Fact]
    public void Moi_ma_cua_ContentPermissions_deu_co_trong_PermissionCodes()
    {
        var known = PermissionCodes.All.ToHashSet(StringComparer.Ordinal);

        var unknown = ContentPermissions.All.Where(code => !known.Contains(code)).ToList();

        Assert.True(unknown.Count == 0,
            "ContentPermissions khai mã quyền không có trong PermissionCodes của Identity (gõ sai? Admin vẫn qua nên "
          + "kiểm tay bằng Admin không thấy): " + string.Join(", ", unknown));
    }

    /// <summary>
    /// Canh gác chống "lưới giả", cùng bài học với <see cref="PermissionCodeUsageTests.PermissionCodes_doc_duoc_du_17_ma"/>:
    /// <c>ContentPermissions.All</c> bỏ sót một hằng thì rule trên xanh vĩnh viễn với chính cái hằng bị bỏ sót.
    /// So số phần tử của mảng với số hằng <c>public const string</c> đọc bằng reflection — thêm hằng mà quên thêm vào
    /// <c>All</c> là đỏ ngay.
    /// </summary>
    [Fact]
    public void All_liet_ke_du_moi_hang_cua_ContentPermissions()
    {
        var constants = typeof(ContentPermissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(constants.Count, ContentPermissions.All.Length);
        Assert.Empty(constants.Except(ContentPermissions.All, StringComparer.Ordinal));
    }
}
