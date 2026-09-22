using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IFeedStore"/> bằng <c>FromSql</c> — chuỗi NỘI SUY của <c>FromSql</c> là tham số hóa (mỗi <c>{…}</c>
/// thành một <c>@p</c>), còn <c>FromSqlRaw</c> với <c>$"…"</c> thì KHÔNG (luật 3 của hướng dẫn B+C+D). Mảng id truyền nguyên
/// <c>Guid[]</c>, Npgsql ánh xạ sang <c>uuid[]</c> — không bao giờ nối chuỗi id.
///
/// Ba chỗ EF/Postgres phải để ý, cả ba đều có lý do ở đây chứ không phải thói quen:
/// <list type="number">
/// <item><b>Sắp lại sau khi bọc</b>: global query filter (<c>status &lt;&gt; 'deleted'</c>) bọc câu thô thành subquery
/// (Npgsql cho bọc cả câu mở bằng <c>WITH</c>; Postgres 16 inline CTE dùng một lần), và thứ tự của subquery không được bảo
/// đảm ở câu ngoài. <c>OrderByDescending</c> sau <c>FromSql</c> thành <c>ORDER BY</c> ngoài cùng; planner thấy subquery đã
/// sắp nên không thêm <c>Sort</c> — <c>Sort</c> duy nhất là top-N trên ≤ <c>số nguồn × take</c> dòng của LATERAL, đúng hình
/// dạng Đ-4.7 (<c>EXPLAIN</c> ở "Thực tế thi công" C2).</item>
/// <item><b>Cursor null ép kiểu trong SQL</b> (<c>::timestamptz</c>, <c>::uuid</c>): Postgres không suy được kiểu của một
/// tham số <c>NULL</c> trần trong <c>IS NULL</c>.</item>
/// <item><b>Literal <c>'published'</c>, <c>'public'</c>, <c>'friends'</c> viết thẳng</b>, không nội suy: nội suy là tham số,
/// và planner không chứng minh được điều kiện của index MỘT PHẦN (<c>WHERE status = 'published'</c>) từ một tham số — mất
/// <c>idx_posts_author_created</c> / <c>idx_posts_public_recent</c>. Literal lệch <c>LowercaseEnum</c> thì
/// <c>FeedStoreTests</c> đỏ (không bài nào khớp).</item>
/// </list>
/// </summary>
public sealed class FeedStore(ContentDbContext db) : IFeedStore
{
    public async Task<IReadOnlyList<Post>> NetworkPageAsync(
        Guid me, FeedSources sources, PostCursor? cursor, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Mảng rỗng chứ không null: unnest('{}') trả 0 dòng — đúng; null là lỗi kiểu.
        Guid[] followingOnly = [.. sources.FollowingOnly];
        Guid[] friends = [.. sources.Friends];
        DateTimeOffset? cursorAt = cursor?.CreatedAt;
        Guid? cursorId = cursor?.PostId;

        // lvl: 1 = chỉ theo dõi · 2 = bạn bè · 3 = chính mình (Đ-4.7 nguyên văn).
        return await db.Posts
            .FromSql($"""
                WITH src(author_id, lvl) AS (
                    SELECT unnest({followingOnly}::uuid[]), 1
                    UNION ALL SELECT unnest({friends}::uuid[]), 2
                    UNION ALL SELECT {me}::uuid, 3
                )
                SELECT p.*
                FROM src
                CROSS JOIN LATERAL (
                    SELECT * FROM content.posts p
                    WHERE p.author_id = src.author_id
                      AND p.status = 'published'
                      AND (src.lvl = 3 OR p.privacy = 'public' OR (src.lvl = 2 AND p.privacy = 'friends'))
                      AND ({cursorAt}::timestamptz IS NULL
                           OR (p.created_at, p.post_id) < ({cursorAt}::timestamptz, {cursorId}::uuid))
                    ORDER BY p.created_at DESC, p.post_id DESC
                    LIMIT {take}
                ) p
                ORDER BY p.created_at DESC, p.post_id DESC
                LIMIT {take}
                """)
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.PostId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Post>> SuggestedPageAsync(
        Guid me, PostCursor? cursor, int take, CancellationToken ct)
    {
        DateTimeOffset? cursorAt = cursor?.CreatedAt;
        Guid? cursorId = cursor?.PostId;

        // Hai vế đầu của WHERE khớp NGUYÊN VĂN điều kiện của idx_posts_public_recent — lệch một chữ là planner bỏ index.
        return await db.Posts
            .FromSql($"""
                SELECT p.*
                FROM content.posts p
                WHERE p.status = 'published' AND p.privacy = 'public'
                  AND p.author_id <> {me}::uuid
                  AND ({cursorAt}::timestamptz IS NULL
                       OR (p.created_at, p.post_id) < ({cursorAt}::timestamptz, {cursorId}::uuid))
                ORDER BY p.created_at DESC, p.post_id DESC
                LIMIT {take}
                """)
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.PostId)
            .ToListAsync(ct);
    }
}
