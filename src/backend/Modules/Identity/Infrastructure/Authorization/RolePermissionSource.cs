using Microsoft.EntityFrameworkCore;
using SocialApp.SharedKernel.Authorization;

namespace SocialApp.Modules.Identity.Infrastructure.Authorization;

/// <summary>
/// Dịch role CODE (chuỗi trong token) → role_id → tập mã quyền. Phép dịch nằm gọn ở đây — token và policy
/// handler từ đầu đến cuối chỉ biết chuỗi (Mục 3.1). ADMIN ra tập rỗng là ĐÚNG: tầng 2 không bao giờ hỏi
/// nguồn này cho ADMIN (Mục 3.2) — đừng "sửa" bằng cách seed quyền cho ADMIN.
///
/// Scoped vì dùng DbContext. PermissionCache (singleton) tự mở scope để gọi — đừng inject nguồn này thẳng vào
/// một singleton (captive dependency).
/// </summary>
internal sealed class RolePermissionSource(IdentityDbContext db) : IRolePermissionSource
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(string roleCode, CancellationToken ct = default)
    {
        var codes = await (
            from r in db.Roles
            where r.Code == roleCode
            join rp in db.RolePermissions on r.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.PermissionId
            select p.Code).ToListAsync(ct);

        return codes.ToHashSet(StringComparer.Ordinal);
    }
}
