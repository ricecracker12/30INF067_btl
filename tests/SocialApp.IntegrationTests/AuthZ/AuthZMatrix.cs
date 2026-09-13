namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// BẢNG của AuthZ matrix (Mục 10.2). Mỗi giai đoạn CHỈ thêm dòng vào đây — không sửa
/// <see cref="AuthZMatrixTests"/>, <see cref="AuthZCase"/> hay <see cref="AuthZApiFactory"/>.
///
/// Kỳ vọng viết tay theo Mục 10.2 và hợp đồng, CỐ Ý không đọc lại PermissionCodes/RoleCodes: test dùng chung
/// nguồn với code thì code sai kiểu gì test cũng sai theo và vẫn xanh. Mã kỳ vọng không lấy từ output.
/// </summary>
public static class AuthZMatrix
{
    public static readonly AuthZCase[] Cases =
    [
        // B3 điền các dòng GĐ1 vào đây. GĐ2 trở đi CHỈ thêm dòng, không sửa file nào khác.
    ];

    /// <summary>
    /// Chỉ đưa Id vào MemberData, không đưa cả record: xUnit 2 không serialize được record chứa delegate
    /// nên sẽ gộp mọi dòng thành MỘT test trong Test Explorer. Đưa chuỗi thì mỗi dòng là một test riêng.
    /// </summary>
    public static IEnumerable<object[]> Ids => Cases.Select(c => new object[] { c.Id });
}
