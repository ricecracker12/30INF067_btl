using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure.Configurations;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Content.Infrastructure.Moderation;

/// <summary>
/// Provider BÀI của hợp đồng ghi <see cref="IModerationTargets"/> (Đ-6.3, Đ-6.12, Đ-6.14 — C2 GĐ6). Bình luận thêm một provider
/// riêng sau khi GĐ3 merge.
///
/// <b>Ghi</b> (ẩn/khôi phục): <see cref="NpgsqlCommand"/> tham số hóa trên CHÍNH <c>tx.Connection</c> + <c>tx</c> của Moderation — không
/// đụng <see cref="ContentDbContext"/> (đó là kết nối thứ hai, đúng lỗi R6-06: bài ẩn rồi mà báo cáo rollback). Một câu
/// <c>UPDATE … WHERE status = 'published' RETURNING</c>; 0 dòng thì <c>SELECT status</c> chỉ để GIẢI THÍCH vì sao — không đọc-rồi-ghi.
/// <c>updated_at</c> của lần ẩn chính là <c>hiddenAt</c> (Mục 4: không có cột <c>hidden_at</c>).
///
/// <b>Đọc</b> (ảnh chụp, thấy-được): qua <see cref="ContentDbContext"/> — đọc bảng của chính module.
/// </summary>
internal sealed class ContentModerationTargets(ContentDbContext db, IFriendshipReader friendships, TimeProvider clock)
    : IModerationTargetProvider
{
    private static readonly string Published = LowercaseEnum.Name(PostStatus.Published);
    private static readonly string Hidden = LowercaseEnum.Name(PostStatus.Hidden);

    private static readonly string HideSql =
        $"UPDATE content.posts SET status = '{Hidden}', hidden_reason = $2, updated_at = $3 " +
        $"WHERE post_id = $1 AND status = '{Published}' RETURNING post_id";

    private static readonly string RestoreSql =
        $"UPDATE content.posts SET status = '{Published}', hidden_reason = NULL, updated_at = $2 " +
        $"WHERE post_id = $1 AND status = '{Hidden}' RETURNING post_id";

    private const string StatusSql = "SELECT status FROM content.posts WHERE post_id = $1";

    public ModerationTargetType Type => ModerationTargetType.Post;

    public async Task<IReadOnlyDictionary<Guid, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, TargetSnapshot>();

        var postIds = ids.ToArray();

        // IgnoreQueryFilters CÓ CHỦ ĐÍCH: Moderator phải thấy cả bài đã xóa mềm để hiểu báo cáo (Mục 8.1). Chỗ khác của module
        // không được sao chép dòng này — đọc cho người dùng luôn qua filter.
        var posts = await db.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => postIds.Contains(p.PostId))
            .Select(p => new { p.PostId, p.AuthorId, p.Body, p.Status, p.CreatedAt, p.EditedAt })
            .ToListAsync(ct);

        var media = (await db.MediaAttachments.AsNoTracking()
                .Where(m => m.OwnerType == MediaOwnerType.Post && postIds.Contains(m.OwnerId))
                .OrderBy(m => m.OwnerId).ThenBy(m => m.Position)
                .Select(m => new { m.OwnerId, m.StorageKey })
                .ToListAsync(ct))
            .ToLookup(m => m.OwnerId, m => m.StorageKey);

        return posts.ToDictionary(
            p => p.PostId,
            p => new TargetSnapshot(
                new ModerationTarget(ModerationTargetType.Post, p.PostId),
                LowercaseEnum.Name(p.Status),
                p.AuthorId,
                p.Body,
                [.. media[p.PostId]],
                p.PostId,
                p.CreatedAt,
                p.EditedAt));
    }

    /// <summary>
    /// Đ-6.12: thấy được mới báo được. Đang <c>published</c> (bài ẩn/xóa → false — không thì <c>POST /reports</c> thành máy dò bài bị
    /// ẩn) VÀ BR-02 qua đúng hàm thuần <see cref="PostVisibility.CanView"/> — không viết lại luật xem.
    /// </summary>
    public async Task<bool> CanViewAsync(Guid actorId, Guid id, CancellationToken ct)
    {
        var post = await db.Posts.AsNoTracking()
            .Where(p => p.PostId == id && p.Status == PostStatus.Published)
            .Select(p => new { p.AuthorId, p.Privacy })
            .SingleOrDefaultAsync(ct);

        if (post is null)
            return false;

        // Chỉ hỏi SocialGraph khi luật cần: bài công khai / riêng tư không phụ thuộc quan hệ.
        var areFriends = post.Privacy == PostPrivacy.Friends && post.AuthorId != actorId
            && await friendships.AreFriendsAsync(actorId, post.AuthorId, ct);

        return PostVisibility.CanView(post.Privacy, post.AuthorId, actorId, areFriends);
    }

    public async Task<HideOutcome> HideAsync(DbTransaction tx, Guid id, string reasonCode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reasonCode);
        var connection = Connection(tx);

        await using (var update = new NpgsqlCommand(HideSql, connection, (NpgsqlTransaction)tx))
        {
            update.Parameters.Add(new NpgsqlParameter { Value = id, NpgsqlDbType = NpgsqlDbType.Uuid });
            update.Parameters.Add(new NpgsqlParameter { Value = reasonCode, NpgsqlDbType = NpgsqlDbType.Varchar });
            update.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow(), NpgsqlDbType = NpgsqlDbType.TimestampTz });
            if (await update.ExecuteScalarAsync(ct) is not null)
                return HideOutcome.Hidden;
        }

        return await StatusAsync(connection, tx, id, ct) == Hidden ? HideOutcome.AlreadyHidden : HideOutcome.NotFound;
    }

    public async Task<RestoreOutcome> RestoreAsync(DbTransaction tx, Guid id, CancellationToken ct)
    {
        var connection = Connection(tx);

        await using (var update = new NpgsqlCommand(RestoreSql, connection, (NpgsqlTransaction)tx))
        {
            update.Parameters.Add(new NpgsqlParameter { Value = id, NpgsqlDbType = NpgsqlDbType.Uuid });
            update.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow(), NpgsqlDbType = NpgsqlDbType.TimestampTz });
            if (await update.ExecuteScalarAsync(ct) is not null)
                return RestoreOutcome.Restored;
        }

        return await StatusAsync(connection, tx, id, ct) == Published ? RestoreOutcome.NotHidden : RestoreOutcome.NotFound;
    }

    /// <summary>Trạng thái hiện tại, đọc TRONG transaction của người gọi. Bài đã xóa mềm → "deleted" → NotFound ở người gọi.</summary>
    private static async Task<string?> StatusAsync(NpgsqlConnection connection, DbTransaction tx, Guid id, CancellationToken ct)
    {
        await using var select = new NpgsqlCommand(StatusSql, connection, (NpgsqlTransaction)tx);
        select.Parameters.Add(new NpgsqlParameter { Value = id, NpgsqlDbType = NpgsqlDbType.Uuid });
        return await select.ExecuteScalarAsync(ct) as string;
    }

    private static NpgsqlConnection Connection(DbTransaction tx) =>
        tx.Connection as NpgsqlConnection
        ?? throw new InvalidOperationException("Transaction của người gọi đã đóng hoặc không phải kết nối Npgsql.");
}
