namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Trạng thái tài khoản — khớp đúng ràng buộc <c>ck_users_status</c> của cột <c>users.status</c>
/// (Mục 4): <c>status IN ('active','locked','disabled','deleted')</c>.
///
/// Dùng <c>const string</c> chứ KHÔNG dùng <c>enum</c>: cột là <c>varchar(20)</c> có CHECK, nên
/// enum chỉ thêm một lớp ánh xạ int/string không cần thiết, và làm giá trị trong DB khác giá trị
/// đọc được trong code. Chuỗi thường, đúng như DB lưu.
/// </summary>
public static class UserStatus
{
    /// <summary>Bình thường — mặc định khi tạo tài khoản.</summary>
    public const string Active = "active";

    /// <summary>Bị khóa tạm thời do đăng nhập sai quá số lần (xem <c>users.locked_until</c>).</summary>
    public const string Locked = "locked";

    /// <summary>Bị vô hiệu hóa bởi quản trị viên.</summary>
    public const string Disabled = "disabled";

    /// <summary>Đã xóa mềm.</summary>
    public const string Deleted = "deleted";

    /// <summary>Bốn giá trị hợp lệ — đúng thứ tự liệt kê trong <c>ck_users_status</c>.</summary>
    public static readonly string[] All = [Active, Locked, Disabled, Deleted];
}
