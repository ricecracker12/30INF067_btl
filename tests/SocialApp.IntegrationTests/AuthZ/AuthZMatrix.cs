using System.Net;

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
        // --- GĐ1 (B3). GĐ2 trở đi CHỈ thêm dòng, không sửa file nào khác. ---

        new("TC-A01", "Gọi endpoint bảo vệ, không kèm JWT", "GĐ1",
            Caller.Anonymous, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

        // TC-A02 tách đôi: "hết hạn" và "sai chữ ký" là hai nhánh validate khác nhau.
        new("TC-A02-expired", "Token đã hết hạn", "GĐ1",
            Caller.ExpiredToken, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

        new("TC-A02-signature", "Token sai chữ ký", "GĐ1",
            Caller.WrongSignature, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

        new("RBAC-01", "ADMIN gọi endpoint đòi quyền bất kỳ — qua dù không có dòng role_permissions", "GĐ1",
            Caller.Admin, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.OK),

        new("RBAC-02", "USER gọi endpoint đòi post.hide", "GĐ1",
            Caller.User, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.Forbidden),

        // Bốn mã gốc xanh được với handler "từ chối mọi vai trò trừ Admin" — dòng này bắt loại hỏng đó.
        new("RBAC-02b", "Đối chứng: MODERATOR gọi endpoint đòi post.hide — vai trò thường CÓ quyền thì phải qua", "GĐ1",
            Caller.Moderator, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.OK),

        // RBAC-02b vẫn để lọt handler "vai trò khác USER thì cho qua" — dòng này bắt nó.
        new("RBAC-02c", "Đối chứng: MODERATOR gọi endpoint đòi user.lock — handler phải xét MÃ QUYỀN, không chỉ vai trò", "GĐ1",
            Caller.Moderator, HttpMethod.Get, "/__test/authz/user-lock", HttpStatusCode.Forbidden),
    ];

    /// <summary>
    /// Chỉ đưa Id vào MemberData, không đưa cả record: xUnit 2 không serialize được record chứa delegate
    /// nên sẽ gộp mọi dòng thành MỘT test trong Test Explorer. Đưa chuỗi thì mỗi dòng là một test riêng.
    /// </summary>
    public static IEnumerable<object[]> Ids => Cases.Select(c => new object[] { c.Id });
}
