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
}
