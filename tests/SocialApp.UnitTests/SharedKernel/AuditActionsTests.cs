using System.Reflection;
using SocialApp.SharedKernel.Audit;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// Canh <see cref="AuditActions"/> (Đ-6.15, A1 GĐ6): mã <c>action</c> là chuỗi người đọc nhật ký lọc theo, nên trùng hay quên
/// liệt kê là dữ liệu audit không lọc được — và không lỗi nào khác báo.
/// </summary>
public sealed class AuditActionsTests
{
    private static List<string> Constants() => typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    [Fact]
    public void Khong_hai_hang_nao_trung_chuoi()
    {
        var constants = Constants();

        Assert.Equal(constants.Count, constants.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Thêm hằng mà quên thêm vào <c>All</c> thì mã đó không được canh ở đâu cả — đỏ ngay.</summary>
    [Fact]
    public void All_liet_ke_du_moi_hang()
    {
        var constants = Constants();

        Assert.Equal(12, constants.Count);   // bảng Đ-6.15 — thêm mã mới thì sửa số này có chủ đích
        Assert.Equal(constants.Count, AuditActions.All.Length);
        Assert.Empty(constants.Except(AuditActions.All, StringComparer.Ordinal));
    }

    /// <summary>Cột <c>action</c> là <c>varchar(50)</c> — chuỗi dài hơn là lỗi 22001 lúc ghi, đúng lúc thao tác quản trị đang chạy.</summary>
    [Fact]
    public void Moi_ma_vua_cot_action()
    {
        Assert.All(AuditActions.All, a => Assert.InRange(a.Length, 1, 50));
    }
}
