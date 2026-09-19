using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.Modules.Profile.Domain;

namespace SocialApp.Modules.Profile.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IProfileStore"/>. <c>internal</c> như <c>IdentityUserStore</c> của GĐ1: ngoài module không
/// ai gọi thẳng được, cửa duy nhất là interface ở <c>Application</c>.
/// </summary>
internal sealed class ProfileStore(ProfileDbContext db) : IProfileStore
{
    /// <summary>
    /// <c>AsNoTracking</c>: D1 chỉ đọc rồi ánh xạ sang DTO, không có gì để ghi lại. Theo dõi thay đổi ở đây chỉ tốn một bản
    /// sao snapshot cho mỗi lời gọi. D2/D3 ghi thì dùng đường riêng của chúng, không mượn phương thức này.
    /// </summary>
    public Task<UserProfile?> FindAsync(Guid userId, CancellationToken ct) =>
        db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);
}
