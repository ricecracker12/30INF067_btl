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

    /// <summary>Thao tác làm hệ thống còn 0 Admin hoạt động — DB đã rollback, không ghi Redis, không audit (Đ-6.7).</summary>
    public static readonly Error LastAdmin = new(
        "admin.last_admin", "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.", 409, "Xung đột dữ liệu",
        Type: LastAdminType);
}
