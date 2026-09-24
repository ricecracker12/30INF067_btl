using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Admin;

/// <summary>
/// Lỗi của các thao tác GHI trên màn quản trị (<c>admin-v1</c>, GĐ6 D3+) — khuôn <see cref="IdentityErrors"/>: một chỗ, không thông
/// điệp nào chứa id hay email. "Không tìm thấy tài khoản" dùng lại <see cref="IdentityErrors.UserNotFound"/> của D2 — một câu cho
/// cả đường đọc lẫn đường ghi.
/// </summary>
public static class AdminErrors
{
    /// <summary>
    /// <c>type</c> của 409 "về 0 Admin" (Đ-6.7). FE phân nhánh theo <c>type</c> (luật frontend Mục 4): D4 gán vai trò trả cùng
    /// 409 này, D5 có 409 khác (<c>confirmation-required</c>, <c>system-role</c>…).
    /// </summary>
    public const string LastAdminType = "urn:socialapp:problem:last-admin";

    /// <summary>
    /// 400 theo trường <c>userId</c>, cùng hình dạng <c>SelfFollow</c> của SocialGraph: khóa xong thì không còn phiên để tự mở lại
    /// (Đ-6.7). Kiểm TRƯỚC mọi I/O.
    /// </summary>
    public static Error SelfLock => Error.Validation("userId", "Không thể tự khóa tài khoản của mình.");

    /// <summary>D4: <c>roleCode</c> không khớp vai trò nào (so chính xác). 400 theo trường, không 404: đích là tài khoản, không phải vai trò.</summary>
    public static Error UnknownRole => Error.Validation("roleCode", "Vai trò không tồn tại.");

    // --- Vai trò (D5, Đ-6.9). Bốn `type` 409 riêng: FE phân nhánh theo `type` (Đ-6.21), không theo câu chữ. ---

    public const string SystemRoleType = "urn:socialapp:problem:system-role";
    public const string RoleCodeTakenType = "urn:socialapp:problem:role-code-taken";
    public const string RoleInUseType = "urn:socialapp:problem:role-in-use";
    public const string ConfirmationRequiredType = "urn:socialapp:problem:confirmation-required";

    public static readonly Error RoleNotFound = new("admin.role_not_found", "Không tìm thấy vai trò.", 404);

    /// <summary>Sửa quyền ADMIN hoặc xóa một trong ba vai trò hệ thống (lớp chặn thứ hai của Đ-6.9; trigger A3 là lớp thứ ba).</summary>
    public static readonly Error SystemRole = new(
        "admin.system_role", "Không thể thay đổi vai trò hệ thống theo cách này.", 409, "Xung đột dữ liệu", Type: SystemRoleType);

    public static readonly Error RoleCodeTaken = new(
        "admin.role_code_taken", "Mã vai trò đã tồn tại.", 409, "Xung đột dữ liệu", Type: RoleCodeTakenType);

    /// <summary>Còn tài khoản mang vai trò — gán họ sang vai trò khác trước (FK RESTRICT của <c>users.role_id</c>).</summary>
    public static readonly Error RoleInUse = new(
        "admin.role_in_use", "Vai trò đang có người dùng, không xóa được.", 409, "Xung đột dữ liệu", Type: RoleInUseType);

    /// <summary>
    /// USER/MODERATOR về 0 quyền (Đ-6.9): seeder coi vai trò không dòng nào là "chưa seed" và cấp lại đủ bộ mặc định ở lần deploy sau,
    /// âm thầm. 400 theo trường <c>permissions</c>.
    /// </summary>
    public static Error RoleNeedsPermission =>
        Error.Validation("permissions", "Vai trò hệ thống phải còn ít nhất một quyền.");

    /// <summary>
    /// 409 xác nhận khi sửa quyền USER/MODERATOR (Đ-6.9) — mang <c>added</c>, <c>removed</c>, <c>affectedUsers</c> qua
    /// <see cref="Error.Extensions"/> (L-D11) để hộp thoại FE hiện đúng số server tính. Trả TRƯỚC mọi ghi (cạm bẫy 4 của D5).
    /// </summary>
    public static Error ConfirmationRequired(IReadOnlyList<string> added, IReadOnlyList<string> removed, int affectedUsers) => new(
        "admin.confirmation_required", "Thay đổi quyền của vai trò hệ thống cần được xác nhận.", 409, "Cần xác nhận",
        Type: ConfirmationRequiredType,
        Extensions: new Dictionary<string, object?>
        {
            ["added"] = added,
            ["removed"] = removed,
            ["affectedUsers"] = affectedUsers,
        });

    /// <summary>Thao tác làm hệ thống còn 0 Admin hoạt động — DB đã rollback, không ghi Redis, không audit (Đ-6.7).</summary>
    public static readonly Error LastAdmin = new(
        "admin.last_admin", "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.", 409, "Xung đột dữ liệu",
        Type: LastAdminType);
}
