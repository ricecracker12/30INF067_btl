using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.SocialGraph.Infrastructure;

/// <summary>
/// Hiện thực <see cref="IFeedSourceReader"/> chưa cache (A5). Cache Redis <c>sg:feed-sources:{userId}</c> là việc của C1.
/// </summary>
internal sealed class FeedSourceReader(SocialGraphDbContext db) : IFeedSourceReader
{
    public async Task<FeedSources> GetAsync(Guid userId, CancellationToken ct = default)
    {
        // Bạn bè: tôi ở một trong hai đầu cặp. Nửa "tôi là min" đi PK, nửa "tôi là max" đi idx_friendships_user_max.
        var friends = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted
                        && (f.UserMinId == userId || f.UserMaxId == userId))
            .Select(f => f.UserMinId == userId ? f.UserMaxId : f.UserMinId)
            .ToListAsync(ct);

        var following = await db.Follows.AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .Select(f => f.FolloweeId)
            .ToListAsync(ct);

        var friendSet = friends.ToHashSet();
        return new FeedSources(friendSet, following.Where(id => !friendSet.Contains(id)).ToHashSet());
    }
}
