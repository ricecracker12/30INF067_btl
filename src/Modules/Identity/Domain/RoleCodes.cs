namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Mã vai trò — hiện thân của quyết định 3.1: <c>roles.code</c> là chuỗi BẤT BIẾN theo hợp đồng API.
/// JWT claim <c>role</c> mang đúng chuỗi này, nên đổi giá trị ở đây là phá hợp đồng với mọi token
/// đang lưu hành, không chỉ đổi một hằng số.
///
/// Ba nơi đọc: seeder (A4), kiểm tra vai trò hệ thống lúc khởi động (A5), và Admin short-circuit
/// ở tầng 2 của khối C. Có hằng số để ba nơi đó không mỗi nơi tự gõ một chuỗi.
/// </summary>
public static class RoleCodes
{
    /// <summary>Người dùng thường — vai trò mặc định khi đăng ký.</summary>
    public const string User = "USER";

    /// <summary>Kiểm duyệt viên — quyền của USER cộng thêm <c>post.hide</c> và <c>report.resolve</c>.</summary>
    public const string Moderator = "MODERATOR";

    /// <summary>
    /// Quản trị viên. CỐ Ý không có dòng <c>role_permissions</c> nào (quyết định 3.2): mọi quyền
    /// đến từ short-circuit ở tầng 2. Đây cũng là lý do A5 phải tồn tại — mất mã này là mất sạch
    /// quyền quản trị mà không có gì để rơi về.
    /// </summary>
    public const string Admin = "ADMIN";

    /// <summary>Ba mã vai trò được seed ở GĐ1 (Mục 5.1). A5 đối chiếu bảng <c>roles</c> với danh sách này.</summary>
    public static readonly string[] All = [User, Moderator, Admin];
}
