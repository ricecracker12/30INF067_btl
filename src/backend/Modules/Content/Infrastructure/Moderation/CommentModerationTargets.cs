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
/// Provider BÌNH LUẬN của <see cref="IModerationTargets"/> (Đ-6.3, Đ-6.12, Đ-6.14) — thêm sau khi GĐ3 merge, đúng chỗ C2 để ngỏ. Từ lúc
/// này <c>Supports(Comment)</c> đúng: <c>POST /reports</c> nhận bình luận, <c>PATCH /reports</c> ẩn được, khôi phục chạy — không sửa dòng nào
/// của Moderation (L-D13).
///
/// <b>Ghi</b> trên CHÍNH <c>tx.Connection</c> + <c>tx</c> của Moderation (khuôn <see cref="ContentModerationTargets"/>, R6-06), theo đúng luật
/// của <c>CommentStore.SoftDeleteAsync</c>:
/// <list type="number">
/// <item><b>Khóa BÀI trước, bình luận sau</b> (Đ-3.8, DEAD-01) — ngược thứ tự là deadlock <c>40P01</c> với một request xóa/trả lời đan vào.</item>
/// <item><c>UPDATE … WHERE status = 'visible' RETURNING</c>: chỉ đổi đúng 1 dòng mới đổi bộ đếm — hai Moderator hay Moderator + tác giả
/// xóa cùng lúc không trừ hai lần.</item>
/// <item><b>Bộ đếm như xóa:</b> ẩn trừ <c>posts.comment_count</c>, khôi phục cộng lại; <c>reply_count</c> của bình luận cha KHÔNG đổi — xóa
/// cũng không đổi nó, vì nhánh vẫn giữ chỗ (Đ-3.5, Đ-6.14).</item>
/// </list>
/// Không có cột <c>hidden_reason</c> trên <c>comments</c> (chốt 2026-09-25): bình luận bị ẩn đi nhánh "đã xóa" phía người đọc (Đ-6.14), lý do
/// nằm ở thông báo <c>moderation</c> của tác giả và ở nhật ký kiểm toán. <c>reasonCode</c> nhận theo hợp đồng, không lưu ở đây.
///
/// <b>Đọc</b> (ảnh chụp, thấy-được): qua <see cref="ContentDbContext"/>.
/// </summary>
internal sealed class CommentModerationTargets(ContentDbContext db, IFriendshipReader friendships, TimeProvider clock)
    : IModerationTargetProvider
{
    private static readonly string Visible = LowercaseEnum.Name(CommentStatus.Visible);
    private static readonly string Hidden = LowercaseEnum.Name(CommentStatus.Hidden);

    /// <summary>
    /// Trạng thái ảnh chụp theo từ vựng của hợp đồng (<c>TargetSnapshot.status</c> của <c>moderation-v1</c>: <c>published | hidden | deleted</c>
    /// cho nội dung): <c>visible</c> của bình luận đọc là <c>published</c> — không mở lại hợp đồng chỉ vì hai bảng đặt tên khác nhau.
    /// </summary>
    public const string SnapshotVisible = "published";

    private const string PostOfCommentSql = "SELECT post_id FROM content.comments WHERE comment_id = $1";

    private const string LockPostSql = "SELECT post_id FROM content.posts WHERE post_id = $1 FOR UPDATE";

    private static readonly string HideSql =
        $"UPDATE content.comments SET status = '{Hidden}', updated_at = $2 WHERE comment_id = $1 AND status = '{Visible}' RETURNING post_id";

    private static readonly string RestoreSql =
        $"UPDATE content.comments SET status = '{Visible}', updated_at = $2 WHERE comment_id = $1 AND status = '{Hidden}' RETURNING post_id";

    private const string DecrementSql = "UPDATE content.posts SET comment_count = comment_count - 1 WHERE post_id = $1";

    private const string IncrementSql = "UPDATE content.posts SET comment_count = comment_count + 1 WHERE post_id = $1";

    private const string StatusSql = "SELECT status FROM content.comments WHERE comment_id = $1";

    public ModerationTargetType Type => ModerationTargetType.Comment;

    public async Task<IReadOnlyDictionary<Guid, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, TargetSnapshot>();

        var commentIds = ids.ToArray();

        // Bình luận không có query filter; đọc MỌI trạng thái — Moderator phải thấy cả bình luận đã xóa/ẩn để hiểu báo cáo (Mục 8.1).
        var comments = await db.Comments.AsNoTracking()
            .Where(c => commentIds.Contains(c.CommentId))
            .Select(c => new { c.CommentId, c.PostId, c.AuthorId, c.Body, c.Status, c.CreatedAt })
            .ToListAsync(ct);

        return comments.ToDictionary(
            c => c.CommentId,
            c => new TargetSnapshot(
                new ModerationTarget(ModerationTargetType.Comment, c.CommentId),
                c.Status == CommentStatus.Visible ? SnapshotVisible : LowercaseEnum.Name(c.Status),
                c.AuthorId,
                c.Body,
                [],                 // bình luận không có ảnh (GĐ3)
                c.PostId,
                c.CreatedAt,
                null));             // bình luận không sửa được (GĐ3) — không có mốc sửa
    }

    /// <summary>
    /// Đ-6.12: thấy được mới báo được. Bình luận còn <c>visible</c>, bài chứa nó còn <c>published</c>, VÀ người gọi xem được bài theo BR-02
    /// (<see cref="PostVisibility.CanView"/> — cùng luật bình luận thừa kế từ bài, Đ-3.3). Bình luận trong bài đã chuyển riêng tư → false:
    /// không thì <c>POST /reports</c> thành máy dò bình luận của bài riêng tư (đúng lỗ LEAK-01 của GĐ3).
    /// </summary>
    public async Task<bool> CanViewAsync(Guid actorId, Guid id, CancellationToken ct)
    {
        var post = await (
                from c in db.Comments.AsNoTracking()
                join p in db.Posts.AsNoTracking() on c.PostId equals p.PostId
                where c.CommentId == id && c.Status == CommentStatus.Visible && p.Status == PostStatus.Published
                select new { p.AuthorId, p.Privacy })
            .SingleOrDefaultAsync(ct);

        if (post is null)
            return false;

        var areFriends = post.Privacy == PostPrivacy.Friends && post.AuthorId != actorId
            && await friendships.AreFriendsAsync(actorId, post.AuthorId, ct);

        return PostVisibility.CanView(post.Privacy, post.AuthorId, actorId, areFriends);
    }

    public async Task<HideOutcome> HideAsync(DbTransaction tx, Guid id, string reasonCode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reasonCode);
        var batch = new CommentTransaction((NpgsqlTransaction)tx, clock);

        if (await batch.LockPostOfAsync(id, ct) is not { } postId)
            return HideOutcome.NotFound;

        if (await batch.SwitchAsync(HideSql, id, ct))
        {
            await batch.AdjustCountAsync(DecrementSql, postId, ct);
            return HideOutcome.Hidden;
        }

        return await batch.StatusAsync(id, ct) == Hidden ? HideOutcome.AlreadyHidden : HideOutcome.NotFound;
    }

    public async Task<RestoreOutcome> RestoreAsync(DbTransaction tx, Guid id, CancellationToken ct)
    {
        var batch = new CommentTransaction((NpgsqlTransaction)tx, clock);

        if (await batch.LockPostOfAsync(id, ct) is not { } postId)
            return RestoreOutcome.NotFound;

        if (await batch.SwitchAsync(RestoreSql, id, ct))
        {
            await batch.AdjustCountAsync(IncrementSql, postId, ct);
            return RestoreOutcome.Restored;
        }

        return await batch.StatusAsync(id, ct) == Visible ? RestoreOutcome.NotHidden : RestoreOutcome.NotFound;
    }

    /// <summary>
    /// Các câu lệnh trên transaction của Moderation. Transaction nằm ở trường, không qua tham số: phương thức nhận <c>DbTransaction</c> ngoài hai
    /// hàm của hợp đồng là dấu hiệu đường ghi tự chế (<c>WriteContractTests</c>, Đ-6.3) — khuôn <c>NotificationStore.GroupTransaction</c>.
    /// </summary>
    private sealed class CommentTransaction(NpgsqlTransaction tx, TimeProvider clock)
    {
        private NpgsqlConnection Connection =>
            tx.Connection ?? throw new InvalidOperationException("Transaction của người gọi đã đóng.");

        /// <summary>
        /// Bài chứa bình luận, rồi KHÓA dòng bài (Đ-3.8: bài trước). <c>post_id</c> của bình luận không bao giờ đổi nên đọc nó không cần khóa.
        /// Bình luận không tồn tại → <c>null</c>.
        /// </summary>
        public async Task<Guid?> LockPostOfAsync(Guid commentId, CancellationToken ct)
        {
            Guid postId;
            await using (var find = Command(PostOfCommentSql, Uuid(commentId)))
            {
                if (await find.ExecuteScalarAsync(ct) is not Guid found)
                    return null;
                postId = found;
            }

            await using var lockPost = Command(LockPostSql, Uuid(postId));
            await lockPost.ExecuteScalarAsync(ct);
            return postId;
        }

        /// <summary>Đổi trạng thái đúng một dòng (điều kiện trạng thái nguồn nằm trong câu) — <c>true</c> khi đã đổi.</summary>
        public async Task<bool> SwitchAsync(string sql, Guid commentId, CancellationToken ct)
        {
            await using var cmd = Command(sql, Uuid(commentId),
                new NpgsqlParameter { Value = clock.GetUtcNow(), NpgsqlDbType = NpgsqlDbType.TimestampTz });
            return await cmd.ExecuteScalarAsync(ct) is not null;
        }

        public async Task AdjustCountAsync(string sql, Guid postId, CancellationToken ct)
        {
            await using var cmd = Command(sql, Uuid(postId));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<string?> StatusAsync(Guid commentId, CancellationToken ct)
        {
            await using var cmd = Command(StatusSql, Uuid(commentId));
            return await cmd.ExecuteScalarAsync(ct) as string;
        }

        private NpgsqlCommand Command(string sql, params NpgsqlParameter[] parameters)
        {
            var cmd = new NpgsqlCommand(sql, Connection, tx);
            cmd.Parameters.AddRange(parameters);
            return cmd;
        }

        private static NpgsqlParameter Uuid(Guid value) => new() { Value = value, NpgsqlDbType = NpgsqlDbType.Uuid };
    }
}
