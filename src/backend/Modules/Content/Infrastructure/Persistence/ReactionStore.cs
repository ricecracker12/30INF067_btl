using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Content.Application.Reactions;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure.Configurations;

namespace SocialApp.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// C2 + C3 + C4 GĐ3 — khuôn giao dịch Đ-3.8 cho cảm xúc trên bài VÀ bình luận (chỉ khác bảng — <see cref="InteractionTarget"/>),
/// cùng đường đọc <c>myReaction</c> theo lô. Không <c>SaveChanges</c> nào: override của <see cref="ContentDbContext"/> đóng dấu
/// <c>updated_at</c>, và bộ đếm không phải nội dung.
///
/// Không log gì ở đây (C2): người gọi đã có đủ ngữ cảnh, và store không được lộ nội dung request hay tên người dùng.
/// </summary>
public sealed class ReactionStore(ContentDbContext db) : IReactionStore, IReactionReader
{
    private const string InsertSql =
        "insert into content.reactions (user_id, target_type, target_id, type, created_at, updated_at) "
      + "values (@me, @tt, @id, @type, @now, @now)";

    private const string UpdateSql =
        "update content.reactions set type = @type, updated_at = @now "
      + "where user_id = @me and target_type = @tt and target_id = @id";

    private const string DeleteSql =
        "delete from content.reactions where user_id = @me and target_type = @tt and target_id = @id";

    /// <summary>
    /// Thứ tự năm bước là Đ-3.8, không thương lượng:
    /// <list type="number">
    /// <item><b>Khóa dòng đối tượng</b> (<c>FOR UPDATE</c>) — không chỉ dựa PK của <c>reactions</c>. Hai tab của CÙNG một người
    /// bấm lần đầu: không khóa thì cả hai thấy "chưa có" ở bước 2, cả hai INSERT, một cái nổ <c>23505</c> thành 500 (COUNT-02).
    /// Có khóa thì cái sau chờ, rồi thấy dòng của cái trước.</item>
    /// <item>Đọc cảm xúc hiện tại — SAU khóa, nên nó là sự thật cho tới COMMIT.</item>
    /// <item><see cref="ReactionTransition.Apply"/> quyết định bốn nhánh.</item>
    /// <item>Ghi dòng <c>reactions</c>, rồi <b>một câu</b> SQL nguyên tử trên <c>jsonb</c> (<see cref="ReactionCountsSql"/>).</item>
    /// <item>Trả bộ đếm vừa ghi (RETURNING) — không đọc lại lần nữa.</item>
    /// </list>
    /// Cảm xúc chỉ khóa ĐÚNG dòng đối tượng (bài hoặc bình luận) — không khóa bài khi đối tượng là bình luận, nên không tham gia
    /// thứ tự khóa bài → bình luận của <see cref="CommentStore"/>.
    /// </summary>
    public async Task<ReactionApplied?> ApplyAsync(
        ReactionTargetType targetType, Guid targetId, Guid actorId, ReactionType? desired, DateTimeOffset now, CancellationToken ct)
    {
        var target = InteractionTarget.Of(targetType);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 1. Khóa. 0 dòng → không tồn tại / đã xóa / bị ẩn → người gọi trả 404. Transaction rollback khi dispose.
        var locked = await db.Database.SqlQueryRaw<Guid>(target.LockSql, new NpgsqlParameter("id", targetId)).ToListAsync(ct);
        if (locked.Count == 0)
            return null;

        // 2.
        var current = await db.Reactions.AsNoTracking()
            .Where(r => r.UserId == actorId && r.TargetType == targetType && r.TargetId == targetId)
            .Select(r => (ReactionType?)r.Type)
            .SingleOrDefaultAsync(ct);

        // 3.
        var change = ReactionTransition.Apply(current, desired);

        // 4a. Dòng reactions.
        var rowSql = change.Write switch
        {
            ReactionWrite.Insert => InsertSql,
            ReactionWrite.Update => UpdateSql,
            ReactionWrite.Delete => DeleteSql,
            _ => null,
        };
        if (rowSql is not null)
            await db.Database.ExecuteSqlRawAsync(rowSql, RowParameters(targetType, targetId, actorId, desired, now), ct);

        // 4b + 5. Bộ đếm — hoặc chỉ đọc khi không có gì đổi (PUT cùng loại lần hai, DELETE khi chưa thả).
        string sql;
        var parameters = new List<NpgsqlParameter> { new("id", targetId) };
        if (change.Decrement is null && change.Increment is null)
        {
            sql = ReactionCountsSql.Read(target);
        }
        else
        {
            sql = ReactionCountsSql.Update(target, change.Decrement is not null, change.Increment is not null);
            if (change.Decrement is { } dec)
                parameters.Add(ReactionCountsSql.Key("dec", dec));
            if (change.Increment is { } inc)
                parameters.Add(ReactionCountsSql.Key("inc", inc));
        }

        var counts = await db.Database.SqlQueryRaw<string>(sql, [.. parameters]).ToListAsync(ct);
        await tx.CommitAsync(ct);

        return new ReactionApplied(change, Parse(counts.Single()));
    }

    /// <summary>
    /// C4 (Đ-3.11): MỘT câu cho cả trang, đi thẳng vào PK <c>(user_id, target_type, target_id)</c> vì <c>user_id</c> là cột đầu.
    /// Trang rỗng → không câu nào.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ReactionType>> GetMineAsync(
        Guid actorId, ReactionTargetType targetType, IReadOnlyCollection<Guid> targetIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targetIds);

        if (targetIds.Count == 0)
            return new Dictionary<Guid, ReactionType>();

        return await db.Reactions.AsNoTracking()
            .Where(r => r.UserId == actorId && r.TargetType == targetType && targetIds.Contains(r.TargetId))
            .ToDictionaryAsync(r => r.TargetId, r => r.Type, ct);
    }

    private static object[] RowParameters(
        ReactionTargetType targetType, Guid targetId, Guid actorId, ReactionType? desired, DateTimeOffset now)
    {
        var parameters = new List<object>
        {
            new NpgsqlParameter("me", actorId),
            new NpgsqlParameter("tt", LowercaseEnum.Name(targetType)),
            new NpgsqlParameter("id", targetId),
            new NpgsqlParameter("now", now),
        };
        if (desired is { } type)
            parameters.Add(new NpgsqlParameter("type", LowercaseEnum.Name(type)));
        return [.. parameters];
    }

    private static Dictionary<string, int> Parse(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
}
