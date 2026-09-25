using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Application.Admin.Users;
using SocialApp.SharedKernel.Text;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IAdminUserQueries"/> (GĐ6 D2). LINQ chứ không SQL thô: EF gắn tham số của <c>LIKE</c> theo kiểu
/// của cột (<c>citext</c>), nên tiền tố không phân biệt hoa thường mà không cần <c>lower()</c> — <c>lower()</c> sẽ bỏ qua index
/// unique của <c>email</c> (cùng bài học <c>FindForLoginAsync</c>, AC-01b).
///
/// Không thêm index <c>created_at</c>: màn Admin tần suất thấp (PTTK UC-19), bảng <c>users</c> của đồ án nhỏ. Khi cần thì một
/// index <c>(created_at DESC, user_id DESC)</c> khớp đúng keyset này.
/// </summary>
internal sealed class AdminUserQueries(IdentityDbContext db) : IAdminUserQueries
{
    public async Task<IReadOnlyList<AdminUserRow>> ListAsync(
        AdminUserFilter filter, AdminUserCursor? after, int take, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();

        if (filter.EmailPrefix is { } prefix)
        {
            // Dựng mẫu NGOÀI biểu thức: EF chỉ tham số hóa giá trị, không dịch được lời gọi LikePattern sang SQL.
            var pattern = LikePattern.StartsWith(prefix);
            users = users.Where(u => EF.Functions.Like(u.Email, pattern, LikePattern.EscapeCharacter));
        }

        if (filter.Status is { } status)
            users = users.Where(u => u.Status == status);

        if (after is { } at)
        {
            users = users.Where(u =>
                u.CreatedAt < at.CreatedAt
                || (u.CreatedAt == at.CreatedAt && u.UserId.CompareTo(at.UserId) < 0));
        }

        var rows =
            from u in users
            join r in db.Roles on u.RoleId equals r.RoleId
            select new { User = u, Role = r };

        if (filter.RoleCode is { } roleCode)
            rows = rows.Where(x => x.Role.Code == roleCode);

        return await rows
            .OrderByDescending(x => x.User.CreatedAt)
            .ThenByDescending(x => x.User.UserId)
            .Take(take)
            .Select(x => new AdminUserRow(
                x.User.UserId, x.User.Email, x.Role.Code, x.Role.DisplayName, x.User.Status,
                x.User.EmailVerifiedAt, x.User.LockedUntil, x.User.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<AdminUserRow?> FindAsync(Guid userId, CancellationToken ct) =>
        (from u in db.Users.AsNoTracking()
         join r in db.Roles on u.RoleId equals r.RoleId
         where u.UserId == userId
         select new AdminUserRow(
             u.UserId, u.Email, r.Code, r.DisplayName, u.Status, u.EmailVerifiedAt, u.LockedUntil, u.CreatedAt))
        .SingleOrDefaultAsync(ct);
}
