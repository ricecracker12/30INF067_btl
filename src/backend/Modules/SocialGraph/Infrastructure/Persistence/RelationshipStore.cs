using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    /// Tên PK cặp trên <c>socialgraph.friendships</c>, do EF sinh theo quy ước và migration
    /// <c>InitialSocialGraph</c> ghi ra. Bắt đúng một constraint (nếp <c>PostStore.StorageKeyUniqueIndex</c>):
    /// CHECK <c>ck_friendships_*</c> cũng là lỗi DB nhưng là lỗi của CHÍNH TA — che thành 409 "đã có quan hệ"
    /// là biến lỗi lập trình thành thông báo sai.
    /// </summary>
    private const string FriendshipsPrimaryKey = "PK_friendships";

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

    /// <summary>
    /// Bắt <b>đúng một</b> constraint. Vi phạm CHECK (tự gửi lọt xuống, requester ngoài cặp) cũng là
    /// <c>DbUpdateException</c>, nhưng chúng phải thành 500 — lưới "khác mình trước DB" mới bắt được.
    /// </summary>
    public async Task<bool> AddRequestAsync(Friendship friendship, CancellationToken ct)
    {
        db.Friendships.Add(friendship);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FriendshipsPrimaryKey,
        })
        {
            // Entity đã Add mà không lưu được thì KHÔNG được nằm lại trong tracker: DbContext là scoped theo
            // request, và một lần SaveChanges khác trong cùng request sẽ cố INSERT lại.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// Một câu, không cửa sổ race. Vế <c>RequesterId == requesterId</c> là lưới
    /// <c>TC-A03-friend-self-accept</c>: thiếu nó thì người gửi tự biến lời mời của mình thành tình bạn.
    /// </summary>
    public async Task<bool> AcceptIncomingAsync(
        FriendPair pair, Guid requesterId, DateTimeOffset now, CancellationToken ct)
    {
        var changed = await db.Friendships
            .Where(f => f.UserMinId == pair.Min && f.UserMaxId == pair.Max
                     && f.Status == FriendshipStatus.Pending
                     && f.RequesterId == requesterId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Status, FriendshipStatus.Accepted)
                .SetProperty(f => f.AcceptedAt, now)
                .SetProperty(f => f.UpdatedAt, now), ct);

        return changed == 1;
    }

    /// <summary>Một câu. Không vế <c>RequesterId</c>: hủy và từ chối dùng chung endpoint, chiều nào cũng xóa.</summary>
    public async Task<bool> DeletePendingAsync(FriendPair pair, CancellationToken ct)
    {
        var changed = await db.Friendships
            .Where(f => f.UserMinId == pair.Min && f.UserMaxId == pair.Max
                     && f.Status == FriendshipStatus.Pending)
            .ExecuteDeleteAsync(ct);

        return changed > 0;
    }

    /// <summary>Một câu. Lời mời <c>pending</c> không khớp vế trạng thái nên không bị đụng.</summary>
    public async Task<bool> DeleteAcceptedAsync(FriendPair pair, CancellationToken ct)
    {
        var changed = await db.Friendships
            .Where(f => f.UserMinId == pair.Min && f.UserMaxId == pair.Max
                     && f.Status == FriendshipStatus.Accepted)
            .ExecuteDeleteAsync(ct);

        return changed > 0;
    }

    /// <summary>
    /// Chiếu <c>other</c> trong SQL (không chuẩn hóa lại bằng <c>CompareTo</c> — dòng đã là cặp chuẩn hóa).
    /// <c>accepted_at</c> nullable trên entity; CHECK buộc nó khác null khi <c>accepted</c>. Map sau <c>ToList</c>
    /// vì <c>DateTimeOffset?</c> không gán được vào <see cref="FriendListRow"/> trong biểu thức EF.
    /// </summary>
    public async Task<IReadOnlyList<FriendListRow>> ListFriendsAsync(
        Guid me, FriendCursor? cursor, int take, CancellationToken ct)
    {
        var query = db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted
                     && (f.UserMinId == me || f.UserMaxId == me));

        if (cursor is { } at)
        {
            query = query.Where(f =>
                f.AcceptedAt < at.Since
                || (f.AcceptedAt == at.Since
                    && (f.UserMinId == me ? f.UserMaxId : f.UserMinId).CompareTo(at.OtherUserId) < 0));
        }

        var rows = await query
            .OrderByDescending(f => f.AcceptedAt)
            .ThenByDescending(f => f.UserMinId == me ? f.UserMaxId : f.UserMinId)
            .Take(take)
            .Select(f => new
            {
                OtherUserId = f.UserMinId == me ? f.UserMaxId : f.UserMinId,
                Since = f.AcceptedAt,
            })
            .ToListAsync(ct);

        return rows.Select(r => new FriendListRow(
            r.OtherUserId,
            r.Since ?? throw new InvalidOperationException("Quan hệ accepted thiếu accepted_at."))).ToList();
    }

    /// <summary>
    /// SQL thô có nội suy là tham số hóa (EF <c>FormattableString</c>). <c>ON CONFLICT DO NOTHING</c> nuốt PK
    /// <c>PK_follows</c> thành 0 dòng — lần theo dõi thứ hai không phải lỗi. CHECK <c>ck_follows_not_self</c> không
    /// phải conflict: nó ném, và service phải chặn tự theo dõi trước khi tới đây.
    /// </summary>
    public async Task<bool> AddFollowAsync(
        Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"INSERT INTO socialgraph.follows (follower_id, followee_id, created_at) VALUES ({followerId}, {followeeId}, {now}) ON CONFLICT DO NOTHING",
            ct);

        return inserted == 1;
    }

    /// <summary>Một câu. Chiều ngược (<c>followee</c> theo dõi lại) không khớp vế <c>follower_id</c>.</summary>
    public async Task<bool> DeleteFollowAsync(Guid followerId, Guid followeeId, CancellationToken ct)
    {
        var changed = await db.Follows
            .Where(f => f.FollowerId == followerId && f.FolloweeId == followeeId)
            .ExecuteDeleteAsync(ct);

        return changed > 0;
    }

    public async Task<IReadOnlyList<FriendListRow>> ListRequestsAsync(
        Guid me, bool incoming, FriendCursor? cursor, int take, CancellationToken ct)
    {
        var query = db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending
                     && (f.UserMinId == me || f.UserMaxId == me));

        query = incoming
            ? query.Where(f => f.RequesterId != me)
            : query.Where(f => f.RequesterId == me);

        if (cursor is { } at)
        {
            query = query.Where(f =>
                f.CreatedAt < at.Since
                || (f.CreatedAt == at.Since
                    && (f.UserMinId == me ? f.UserMaxId : f.UserMinId).CompareTo(at.OtherUserId) < 0));
        }

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.UserMinId == me ? f.UserMaxId : f.UserMinId)
            .Take(take)
            .Select(f => new FriendListRow(
                f.UserMinId == me ? f.UserMaxId : f.UserMinId,
                f.CreatedAt))
            .ToListAsync(ct);
    }
}
