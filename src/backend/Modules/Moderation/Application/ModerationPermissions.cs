namespace SocialApp.Modules.Moderation.Application;

/// <summary>
/// Bốn mã quyền tầng 2 mà module Moderation dùng, dưới dạng hằng chuỗi CỦA CHÍNH MODULE.
///
/// Vì sao không <c>using SocialApp.Modules.Identity.Domain.PermissionCodes</c>: luật 4 cấm import chéo module
/// (<c>ModuleBoundaryTests</c> canh). Mã quyền là chuỗi trong HỢP ĐỒNG giữa hai module — Moderation bám vào
/// chuỗi, không bám vào kiểu của Identity.
///
/// Cái giá là gõ sai không bị compiler bắt, và Admin VẪN QUA (short-circuit của <c>PermissionChecks.IsAllowedAsync</c>
/// không nhìn mã) nên người test tay bằng Admin thấy "chạy tốt" trong khi MODERATOR bị chặn vĩnh viễn. Lưới cho đúng
/// chuyện đó: <c>ModerationPermissionsTests</c> ở ArchitectureTests — project test tham chiếu được cả hai module —
/// khẳng định mọi hằng ở đây nằm trong <c>PermissionCodes.All</c>.
///
/// <c>role.manage</c>, <c>user.lock</c>, <c>user.unlock</c>, <c>role.assign</c> KHÔNG ở đây: chúng thuộc controller
/// quản trị của Identity (nhóm <c>admin-v1</c>), đọc thẳng <c>PermissionCodes</c>.
/// </summary>
public static class ModerationPermissions
{
    /// <summary>Báo cáo nội dung hoặc tài khoản vi phạm (<c>POST /reports</c>).</summary>
    public const string ReportCreate = "report.create";

    /// <summary>Xem hàng đợi và xử lý báo cáo (<c>GET /reports</c>, <c>PATCH /reports/{reportId}</c>).</summary>
    public const string ReportResolve = "report.resolve";

    /// <summary>
    /// Ẩn và khôi phục nội dung vi phạm (<c>POST /moderation/targets/{targetType}/{targetId}/restore</c>). Quyết định
    /// <c>hide</c> của một báo cáo kiểm THÊM mã này ngoài <see cref="ReportResolve"/> — tầng 2 kép của D7 (Đ-6.13).
    /// </summary>
    public const string PostHide = "post.hide";

    /// <summary>Xem nhật ký kiểm toán toàn hệ thống (<c>GET /admin/audit-logs</c> — chỉ ADMIN theo ma trận PTTK).</summary>
    public const string AuditRead = "audit.read";

    /// <summary>Cả bốn mã, cho test kiến trúc đọc — thêm hằng mới thì thêm vào đây, nếu không nó không được canh.</summary>
    public static readonly string[] All = [ReportCreate, ReportResolve, PostHide, AuditRead];
}
