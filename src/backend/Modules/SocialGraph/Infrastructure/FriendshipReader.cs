using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.SocialGraph.Infrastructure;

/// <summary>
/// Hiện thực thật của <see cref="IFriendshipReader"/> (Đ-4.3): một lần tra PK cặp chuẩn hóa, không cache.
/// Hủy kết bạn phải có hiệu lực ngay với <c>GET /posts/{id}</c> (FRD-09).
/// </summary>
internal sealed class FriendshipReader(SocialGraphDbContext db) : IFriendshipReader
{
    public Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        // Mình không phải "bạn" của mình — cùng hợp đồng với AlwaysStrangers, và FriendPair.Of sẽ ném nếu đi tiếp.
        if (userId == otherUserId)
            return Task.FromResult(false);

        var pair = FriendPair.Of(userId, otherUserId);
        return db.Friendships.AsNoTracking().AnyAsync(f =>
            f.UserMinId == pair.Min
            && f.UserMaxId == pair.Max
            && f.Status == FriendshipStatus.Accepted, ct);
    }
}
