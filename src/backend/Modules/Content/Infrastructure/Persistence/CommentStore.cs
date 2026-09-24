using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Comments;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure.Configurations;

namespace SocialApp.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="ICommentStore"/> (D1–D4, C3). Hai hàm ghi theo khuôn Đ-3.8: transaction tường minh, khóa dòng trước khi
/// đổi, bộ đếm bằng <c>x = x ± 1</c> nguyên tử — không đọc lên rồi ghi xuống.
///
/// <b>THỨ TỰ KHÓA CỐ ĐỊNH TOÀN MODULE: BÀI TRƯỚC, BÌNH LUẬN SAU</b> (Đ-3.8, DEAD-01). Tạo phản hồi khóa bài rồi khóa cha; xóa bình
/// luận khóa bài rồi mới đổi bình luận. Viết ngược ở một hàm là hai request đan nhau thành deadlock <c>40P01</c> (COUNT-04).
/// </summary>
public sealed class CommentStore(ContentDbContext db) : ICommentStore
{
    private static readonly string LockPublishedPostSql =
        $"select post_id as \"Value\" from content.posts where post_id = @p and {LowercaseEnum.EqualsSql("status", PostStatus.Published)} for update";

    /// <summary>Xóa bình luận khóa bài ở MỌI trạng thái: tác giả xóa được bình luận của mình kể cả khi bài đã xóa/ẩn (Đ-3.3).</summary>
    private const string LockAnyPostSql = "select post_id as \"Value\" from content.posts where post_id = @p for update";

    private static readonly string LockVisibleParentSql =
        $"select comment_id as \"Value\" from content.comments where comment_id = @c and post_id = @p "
      + $"and {LowercaseEnum.EqualsSql("status", CommentStatus.Visible)} for update";

    private const string IncrementCommentCountSql = "update content.posts set comment_count = comment_count + 1 where post_id = @p";
    private const string DecrementCommentCountSql = "update content.posts set comment_count = comment_count - 1 where post_id = @p";
    private const string IncrementReplyCountSql = "update content.comments set reply_count = reply_count + 1 where comment_id = @c";

    private static readonly string SoftDeleteSql =
        $"update content.comments set status = '{LowercaseEnum.Name(CommentStatus.Deleted)}', deleted_at = @now, updated_at = @now "
      + $"where comment_id = @c and author_id = @a and {LowercaseEnum.EqualsSql("status", CommentStatus.Visible)}";

    public Task<Comment?> FindAsync(Guid commentId, CancellationToken ct) =>
        db.Comments.AsNoTracking().SingleOrDefaultAsync(c => c.CommentId == commentId, ct);

    /// <summary>
    /// <c>ParentId == null</c> dịch thành <c>parent_id IS NULL</c> — đúng điều kiện của index một phần
    /// <c>idx_comments_post_roots</c>. Keyset ASC: dòng mới rơi vào CUỐI, không nhân đôi hay nhảy cóc giữa hai trang.
    /// </summary>
    public Task<IReadOnlyList<Comment>> ListRootsAsync(Guid postId, KeysetCursor? cursor, int take, CancellationToken ct) =>
        PageAsync(db.Comments.AsNoTracking().Where(c => c.PostId == postId && c.ParentId == null), cursor, take, ct);

    public Task<IReadOnlyList<Comment>> ListRepliesAsync(Guid parentId, KeysetCursor? cursor, int take, CancellationToken ct) =>
        PageAsync(db.Comments.AsNoTracking().Where(c => c.ParentId == parentId), cursor, take, ct);

    private static async Task<IReadOnlyList<Comment>> PageAsync(
        IQueryable<Comment> query, KeysetCursor? cursor, int take, CancellationToken ct)
    {
        if (cursor is { } at)
            query = query.Where(c => c.CreatedAt > at.CreatedAt
                                  || (c.CreatedAt == at.CreatedAt && c.CommentId.CompareTo(at.Id) > 0));

        return await query
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.CommentId)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Mục 7.2 bước 4 — khóa BÀI → CHA. Cha được kiểm lại TRONG khóa (còn <c>visible</c>, cùng bài): nó có thể vừa bị xóa giữa lúc
    /// service kiểm và lúc này. <c>depth</c> của cha không bao giờ đổi nên không kiểm lại.
    /// </summary>
    public async Task<CommentAddOutcome> AddAsync(Comment comment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comment);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var post = new NpgsqlParameter("p", comment.PostId);
        if ((await db.Database.SqlQueryRaw<Guid>(LockPublishedPostSql, post).ToListAsync(ct)).Count == 0)
            return CommentAddOutcome.PostGone;

        if (comment.ParentId is { } parentId)
        {
            var locked = await db.Database.SqlQueryRaw<Guid>(
                    LockVisibleParentSql, new NpgsqlParameter("c", parentId), new NpgsqlParameter("p", comment.PostId))
                .ToListAsync(ct);
            if (locked.Count == 0)
                return CommentAddOutcome.ParentGone;
        }

        await db.Database.ExecuteSqlRawAsync(IncrementCommentCountSql, [new NpgsqlParameter("p", comment.PostId)], ct);
        if (comment.ParentId is { } parent)
            await db.Database.ExecuteSqlRawAsync(IncrementReplyCountSql, [new NpgsqlParameter("c", parent)], ct);

        // INSERT qua EF: entity Added nên override SaveChanges không đóng dấu gì (nó chỉ chạm Modified).
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Entity đã ghi xong không nằm lại tracker của request — người sau SaveChanges không INSERT lại nó.
        db.ChangeTracker.Clear();
        return CommentAddOutcome.Added;
    }

    /// <summary>
    /// Mục 7.3 — khóa BÀI trước, rồi mới đổi bình luận (Đ-3.8). Điều kiện <c>status = 'visible'</c> trong câu UPDATE cộng "chỉ trừ
    /// khi đổi đúng 1 dòng" chặn trừ hai lần khi hai tab cùng bấm Xóa.
    /// </summary>
    public async Task<bool> SoftDeleteAsync(Comment comment, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comment);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 1. BÀI trước (Đ-3.8). Dòng bài luôn tồn tại (FK), kể cả khi bài đã xóa mềm.
        await db.Database.SqlQueryRaw<Guid>(LockAnyPostSql, new NpgsqlParameter("p", comment.PostId)).ToListAsync(ct);

        // 2. Bình luận sau.
        var changed = await db.Database.ExecuteSqlRawAsync(
            SoftDeleteSql,
            [new NpgsqlParameter("c", comment.CommentId), new NpgsqlParameter("a", actorId), new NpgsqlParameter("now", now)],
            ct);

        if (changed == 1)
            await db.Database.ExecuteSqlRawAsync(DecrementCommentCountSql, [new NpgsqlParameter("p", comment.PostId)], ct);

        await tx.CommitAsync(ct);
        return changed == 1;
    }
}
