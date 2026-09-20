using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IPostStore"/>. Chỗ DUY NHẤT của module chạm <see cref="ContentDbContext"/> cho luồng ghi
/// bài — <c>Application</c> chỉ thấy interface (<c>PersistenceBoundaryTests</c> canh).
/// </summary>
public sealed class PostStore(ContentDbContext db) : IPostStore
{
    /// <summary>
    /// Tên index UNIQUE trên <c>media_attachments.storage_key</c>, do EF sinh theo quy ước và migration
    /// <c>InitialContent</c> ghi ra. Gõ tay ở đây là chấp nhận được vì nó **có test canh**: đổi tên index mà quên sửa
    /// dòng này thì <c>POST-08</c> đỏ ngay (409 rơi thành 500).
    /// </summary>
    private const string StorageKeyUniqueIndex = "IX_media_attachments_storage_key";

    /// <summary>
    /// Một <c>SaveChangesAsync</c> cho cả <c>Add(post)</c> lẫn <c>AddRange(attachments)</c> — EF gói mọi thay đổi của
    /// một lần lưu vào MỘT transaction ngầm, nên không cần <c>BeginTransactionAsync</c> tường minh ở đây.
    ///
    /// Bắt <b>đúng một</b> index. <c>uq_media_owner_position</c> hay vi phạm PK cũng là <c>UniqueViolation</c>, nhưng
    /// chúng là lỗi của CHÍNH TA khi gán <c>position</c>/<c>PostId</c> — che chúng thành 409 "ảnh đã dùng ở bài khác" là
    /// biến một lỗi lập trình thành một thông báo sai cho người dùng, và xóa luôn dấu vết trong log.
    /// </summary>
    public async Task<bool> AddWithMediaAsync(Post post, IReadOnlyList<MediaAttachment> attachments, CancellationToken ct)
    {
        db.Posts.Add(post);
        db.MediaAttachments.AddRange(attachments);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StorageKeyUniqueIndex,
        })
        {
            // Entity đã Add mà không lưu được thì KHÔNG được nằm lại trong tracker: DbContext là scoped theo request,
            // và một lần SaveChanges khác trong cùng request sẽ cố INSERT lại chúng.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// <c>AsNoTracking</c>: đường đọc. Global query filter (<c>status &lt;&gt; 'deleted'</c>) áp tự động — KHÔNG
    /// <c>IgnoreQueryFilters()</c> ở đây (luật 7 của khối D); bài đã xóa "biến mất" là hành vi đúng của D6/D8.
    /// </summary>
    public Task<Post?> FindAsync(Guid postId, CancellationToken ct) =>
        db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.PostId == postId, ct);

    /// <summary>
    /// Keyset của Đ-2.11. Hai mệnh đề đáng chú ý:
    ///
    /// <b>BR-02 trong WHERE.</b> Ba vế dưới đây là bản SQL của <see cref="Application.Posts.PostVisibility.CanView"/>;
    /// <paramref name="areFriends"/> là <c>bool</c> đã tính nên EF gấp nó thành hằng <c>TRUE</c>/<c>FALSE</c> và vế thứ
    /// ba biến mất khỏi câu SQL khi <c>false</c>.
    ///
    /// <b>So sánh keyset.</b> Đ-2.11 viết <c>(created_at, post_id) &lt; (@at, @id)</c> — so sánh BỘ của Postgres, đọc
    /// thẳng <c>idx_posts_author_created</c>. LINQ không có so sánh bộ nên tách thành hai vế; <c>Guid</c> không có toán
    /// tử <c>&lt;</c> trong C# nên dùng <c>CompareTo</c> (xem "Thực tế thi công" của D6 về việc Npgsql có dịch được
    /// không).
    /// </summary>
    public async Task<IReadOnlyList<Post>> ListByAuthorAsync(
        Guid authorId, Guid actorId, bool areFriends, PostCursor? cursor, int take, CancellationToken ct)
    {
        var query = db.Posts
            .AsNoTracking()
            .Where(p => p.AuthorId == authorId)
            .Where(p => p.Privacy == PostPrivacy.Public
                     || p.AuthorId == actorId
                     || (areFriends && p.Privacy == PostPrivacy.Friends));

        if (cursor is { } at)
            query = query.Where(p => p.CreatedAt < at.CreatedAt
                                  || (p.CreatedAt == at.CreatedAt && p.PostId.CompareTo(at.PostId) < 0));

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.PostId)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>
    /// MỘT câu cho cả trang. Lọc thêm <c>OwnerType == Post</c> dù <c>owner_id</c> là UUID v7 gần như không đụng nhau:
    /// bảng ĐA HÌNH (Đ-2.12) nên <c>owner_type</c> là một nửa khóa logic, và <c>idx_media_owner</c> dựng theo đúng cặp
    /// đó — bỏ vế này là câu truy vấn không dùng được index.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachment>>> MediaOfAsync(
        IReadOnlyCollection<Guid> postIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(postIds);

        if (postIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<MediaAttachment>>();

        var rows = await db.MediaAttachments
            .AsNoTracking()
            .Where(m => m.OwnerType == MediaOwnerType.Post && postIds.Contains(m.OwnerId))
            .ToListAsync(ct);

        return rows
            .GroupBy(m => m.OwnerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MediaAttachment>)[.. g.OrderBy(m => m.Position)]);
    }

    /// <summary>
    /// KHÔNG <c>AsNoTracking</c> — đây là bản để sửa; <see cref="SaveAsync"/> dựa vào ChangeTracker để biết cột nào đổi.
    /// Query filter vẫn áp: bài đã xóa mềm trả <c>null</c>.
    /// </summary>
    public Task<Post?> FindForUpdateAsync(Guid postId, CancellationToken ct) =>
        db.Posts.SingleOrDefaultAsync(p => p.PostId == postId, ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
