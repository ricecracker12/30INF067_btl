using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Identity.Infrastructure;

/// <summary>
/// Hiện thực <see cref="IAccountStatusReader"/> (C5 của GĐ6): một câu <c>SELECT user_id … WHERE user_id = ANY(@ids) AND status &lt;&gt;
/// 'active'</c> — Npgsql dịch <c>Contains</c> trên mảng thành <c>= ANY</c>. Scoped vì dùng <see cref="IdentityDbContext"/>.
/// </summary>
internal sealed class AccountStatusReader(IdentityDbContext db) : IAccountStatusReader
{
    public async Task<IReadOnlySet<Guid>> GetInactiveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return new HashSet<Guid>();

        var ids = userIds.Distinct().ToArray();
        var inactive = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.UserId) && u.Status != UserStatus.Active)
            .Select(u => u.UserId)
            .ToListAsync(ct);

        return inactive.ToHashSet();
    }
}
