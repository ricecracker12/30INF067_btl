using SocialApp.Modules.Identity.Domain;
using SocialApp.Modules.Messaging.Application;
using Xunit;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// A4 GĐ5 (Mục 5, Mục 10.5 #5) — bản Messaging của <see cref="SocialGraphPermissionsTests"/>: <see cref="MessagingPermissions"/>
/// là bản sao CHUỖI của mã quyền mà Messaging dùng, lệch được trong im lặng — <c>"message.sned"</c> vẫn compile, Admin vẫn qua.
///
/// Đã thử cho đỏ một lần: đổi tạm <c>MessageSend</c> thành <c>"message.sned"</c> → đỏ đúng mã đó → khôi phục.
/// </summary>
public sealed class MessagingPermissionsTests
{
    [Fact]
    public void Moi_ma_cua_MessagingPermissions_deu_co_trong_PermissionCodes()
    {
        var known = PermissionCodes.All.ToHashSet(StringComparer.Ordinal);

        var unknown = MessagingPermissions.All.Where(code => !known.Contains(code)).ToList();

        Assert.True(unknown.Count == 0,
            "MessagingPermissions khai mã quyền không có trong PermissionCodes của Identity (gõ sai? Admin vẫn qua nên "
          + "kiểm tay bằng Admin không thấy): " + string.Join(", ", unknown));
    }

    [Fact]
    public void All_liet_ke_du_moi_hang_cua_MessagingPermissions()
    {
        var constants = typeof(MessagingPermissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(constants.Count, MessagingPermissions.All.Length);
        Assert.Empty(constants.Except(MessagingPermissions.All, StringComparer.Ordinal));
    }
}
