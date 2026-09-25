namespace SocialApp.SharedKernel.Audit;

/// <summary>
/// Mã <c>action</c> của nhật ký kiểm toán — đúng bảng Đ-6.15 của giai-doan-6.md. Cột là <c>varchar(50)</c>, không CHECK: danh
/// sách sẽ dài thêm ở GĐ8 (xóa tài khoản thay người dùng, xuất dữ liệu) mà không phải migration.
///
/// Đặt ở SharedKernel chứ không ở <c>Moderation.Domain</c> (lệch Đ-6.15, chốt 2026-09-23 — L-A5 của hướng dẫn khối A+C): người
/// GHI audit là Identity (<c>user.*</c>, <c>role.*</c>) và chính SharedKernel (<c>access.denied</c> — handler tầng 2 của C4). Cả
/// hai không được tham chiếu module Moderation (ADR-001), nên hằng phải nằm ở tầng cả ba cùng thấy.
///
/// Thêm mã mới thì thêm vào <see cref="All"/> — <c>AuditActionsTests</c> đỏ nếu quên.
/// </summary>
public static class AuditActions
{
    // --- Kiểm duyệt (target_type: post | comment | user) ---

    /// <summary>Quyết định <c>hide</c> — metadata: <c>reportIds[]</c>, <c>reasonCode</c>, <c>note</c>.</summary>
    public const string ReportHide = "report.hide";

    /// <summary>Quyết định <c>dismiss</c>.</summary>
    public const string ReportDismiss = "report.dismiss";

    /// <summary>Quyết định <c>resolve</c> (chỉ đối tượng người dùng).</summary>
    public const string ReportResolve = "report.resolve";

    /// <summary>Khôi phục đối tượng đã ẩn.</summary>
    public const string ContentRestore = "content.restore";

    // --- Tài khoản (target_type: user) — metadata: user.lock → reason; role.assign → fromRole, toRole. KHÔNG `revocation`:
    //     audit ghi trong transaction, thu hồi chạy sau COMMIT nên lúc ghi chưa biết kết quả (L-D9, sửa 2026-09-24 khi thi công D3).

    public const string UserLock = "user.lock";

    public const string UserUnlock = "user.unlock";

    public const string RoleAssign = "role.assign";

    // --- Vai trò (target_type: role) — metadata: code, added[], removed[], confirmed ---

    public const string RoleCreate = "role.create";

    public const string RoleRename = "role.rename";

    public const string RolePermissions = "role.permissions";

    public const string RoleDelete = "role.delete";

    // --- Truy cập (target_type: endpoint) — metadata: method, routeTemplate (không query string, không id trên đường) ---
    //     Tầng 2 kép trong service (L-D12, L-D18 — GĐ6 D4+) ghi CÙNG mã này nhưng target là đối tượng của thao tác (vd `user`
    //     + id tài khoản đích) và metadata `permission` = quyền còn thiếu: service không biết route, còn đối tượng thì biết.

    /// <summary>Tầng 2 từ chối một endpoint mang <c>[PrivilegedEndpoint]</c> (US-019 AC-03). Tối đa một dòng/phút/(người, route).</summary>
    public const string AccessDenied = "access.denied";

    /// <summary>Mười hai mã của Đ-6.15.</summary>
    public static readonly string[] All =
    [
        ReportHide, ReportDismiss, ReportResolve, ContentRestore,
        UserLock, UserUnlock, RoleAssign,
        RoleCreate, RoleRename, RolePermissions, RoleDelete,
        AccessDenied,
    ];
}
