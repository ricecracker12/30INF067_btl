using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Administration;

/// <summary>
/// Bất biến "luôn còn ≥ 1 Admin hoạt động" (Đ-6.7) — MỘT chỗ cho mọi đường có thể chạm tập Admin: khóa (D3), gán vai trò (D4),
/// và tự xóa tài khoản của GĐ8. Đừng nhân bản câu đếm.
///
/// Hai bước, cả hai TRONG transaction của người gọi:
/// <list type="number">
/// <item><see cref="AcquireAsync"/> — khóa tư vấn, LUÔN lấy cho mọi thao tác ghi của <c>admin-v1</c> lên <c>users</c> (L-D8):
/// quyết định "có lấy không" dựa trên lần đọc vai trò TRƯỚC khi khóa dòng là một lỗ đua. Lấy TRƯỚC mọi khóa dòng — khóa tư vấn
/// sau <c>FOR UPDATE</c> thì hai thao tác chờ nhau ra deadlock <c>40P01</c> (cạm bẫy 2), cùng luật "khóa family trước khóa dòng"
/// của <c>RefreshTokenStore</c>.</item>
/// <item><see cref="EnsureRemainsAsync"/> — đếm SAU khi ghi: không tự suy "thao tác này có giảm số Admin không", cho DB trả lời.
/// "Đếm rồi ghi" sai dưới đồng thời: X khóa Y ‖ Y khóa X, cả hai lần đếm thấy 2, cả hai qua, còn 0 (<c>ADM-C2</c>).</item>
/// </list>
///
/// So <see cref="RoleCodes.Admin"/> (trỏ về <c>SystemRoles.Admin</c>), không <c>role_id = 3</c>: id là chi tiết seed (cạm bẫy 3).
/// Đây là bất biến DỮ LIỆU, không phải kiểm quyền — luật "không so ADMIN ngoài SharedKernel" (Mục 1.3 luật 3) nói về tầng 2.
/// </summary>
internal static class AdminInvariant
{
    // Namespace khóa tư vấn RIÊNG của Identity — khác FamilyLockNamespace = 0x5246 ("RF") của RefreshTokenStore, theo luật ghi ở
    // đầu lớp đó. `grep -rn pg_advisory src/` lúc chọn (D3, 2026-09-24): chỉ có "RF".
    private const int LockNamespace = 0x4144;   // "AD"
    private const int LockKey = 1;              // "admin-invariant"

    /// <summary>Khóa tư vấn tự nhả khi transaction kết thúc. Gọi TRONG transaction, TRƯỚC mọi khóa dòng.</summary>
    public static Task AcquireAsync(IdentityDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockNamespace}, {LockKey})", ct);

    /// <summary><c>true</c> nếu SAU các câu ghi của transaction hiện tại vẫn còn ít nhất một Admin <c>active</c>.</summary>
    public static Task<bool> EnsureRemainsAsync(IdentityDbContext db, CancellationToken ct) =>
        (from u in db.Users
         join r in db.Roles on u.RoleId equals r.RoleId
         where r.Code == RoleCodes.Admin && u.Status == UserStatus.Active
         select u.UserId)
        .AnyAsync(ct);
}
