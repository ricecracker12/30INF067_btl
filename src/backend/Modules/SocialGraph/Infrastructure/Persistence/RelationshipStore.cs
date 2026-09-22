using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.SocialGraph.Application.Relationships;
using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.Modules.SocialGraph.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IRelationshipStore"/>. Chỗ DUY NHẤT của module chạm
/// <see cref="SocialGraphDbContext"/> cho luồng ghi quan hệ — <c>Application</c> chỉ thấy interface
/// (<c>PersistenceBoundaryTests</c> canh).
/// </summary>
public sealed class RelationshipStore(SocialGraphDbContext db) : IRelationshipStore
{
    /// <summary>
    /// <c>AsNoTracking</c>: đường đọc. Không có global query filter — friendships không xóa mềm; "chưa có quan hệ"
    /// và "đã hủy" đều là <c>null</c> (hủy là xóa dòng, Đ-4.14).
    /// </summary>
    public Task<Friendship?> FindFriendshipAsync(FriendPair pair, CancellationToken ct) =>
        db.Friendships.AsNoTracking()
            .SingleOrDefaultAsync(f => f.UserMinId == pair.Min && f.UserMaxId == pair.Max, ct);

    public Task<bool> IsFollowingAsync(Guid followerId, Guid followeeId, CancellationToken ct) =>
        db.Follows.AsNoTracking()
            .AnyAsync(f => f.FollowerId == followerId && f.FolloweeId == followeeId, ct);
}
