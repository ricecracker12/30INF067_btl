using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Application.Roles;

/// <summary>
/// Quyền hiệu lực của một vai trò để HIỂN THỊ (Đ-6.11): <c>GET /me.permissions</c> (D1) và <c>RoleSummary.permissions</c> (D5).
/// Tầng 2 KHÔNG đi qua đây — nó là <c>PermissionHandler</c> + <c>PermissionChecks.IsAllowedAsync</c> của SharedKernel.
///
/// Chỗ duy nhất ngoài SharedKernel so với mã ADMIN (luật 3, Mục 1.3 hướng dẫn khối D): ADMIN không có dòng
/// <c>role_permissions</c> nào (lối tắt tầng 2 — GĐ1 quyết định 2), nên đọc bảng thì ADMIN ra rỗng. Mọi chỗ cần "ADMIN có gì"
/// để vẽ gọi hàm này, không tự viết lại nhánh đó.
/// </summary>
public static class EffectivePermissions
{
    /// <param name="roleCode"><c>roles.code</c> đọc từ DB, không từ claim.</param>
    /// <param name="granted">Mã quyền của vai trò theo <c>role_permissions</c>, đã sắp theo <c>permission_id</c>.</param>
    /// <returns>ADMIN → cả <see cref="PermissionCodes.All"/>; vai trò khác → đúng <paramref name="granted"/>.</returns>
    public static IReadOnlyList<string> For(string roleCode, IReadOnlyList<string> granted) =>
        roleCode == RoleCodes.Admin ? PermissionCodes.All : granted;
}
