namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Bảng nối vai trò ↔ quyền (ENT-10b, bảng <c>role_permissions</c>). Mỗi dòng là một dấu tick trong
/// ma trận Mục 6.7.2 — không có cột nào khác, vì BẢN THÂN SỰ TỒN TẠI CỦA DÒNG chính là quyền.
///
/// Hệ quả cho A4: seeder không thể dựa vào <c>ON CONFLICT DO NOTHING</c> một mình để idempotent —
/// quyền bị Admin gỡ đi sẽ không còn xung đột nào để bỏ qua, và sẽ bị cấp lại. Xem A4.
///
/// ADMIN CỐ Ý KHÔNG CÓ DÒNG NÀO ở bảng này (quyết định 3.2) — mọi quyền đến từ short-circuit tầng 2.
/// </summary>
public sealed class RolePermission
{
    /// <summary>Nửa đầu khóa chính ghép. FK → <c>roles.role_id</c>, ON DELETE CASCADE.</summary>
    public required short RoleId { get; init; }

    /// <summary>Nửa sau khóa chính ghép. FK → <c>permissions.permission_id</c>, ON DELETE CASCADE.</summary>
    public required short PermissionId { get; init; }
}
