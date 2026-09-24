using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IConversationStore"/> (A5, D5). Mọi SQL thô đi qua <c>FromSql</c>/<c>SqlQuery</c>/<c>ExecuteSql</c> với
/// chuỗi nội suy — EF biến mỗi giá trị nội suy thành THAM SỐ, không nối chuỗi (B.10 điều 4). Chỗ duy nhất ghép tên cột là
/// tiền tố <c>user_a</c>/<c>user_b</c> — hai hằng, chọn bằng một <c>bool</c>, không bao giờ từ dữ liệu người dùng.
/// </summary>
internal sealed class ConversationStore(MessagingDbContext db) : IConversationStore
{
    private const string ClientMsgIdIndex = "uq_messages_conv_client_id";
    private const string SeqIndex = "uq_messages_conv_seq";

    public Task<Conversation?> FindAsync(Guid conversationId, CancellationToken ct) =>
        db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId, ct);

    public async Task<(Conversation Conversation, bool Created)> GetOrCreateAsync(
        ConversationPair pair, Guid newId, DateTimeOffset now, CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            insert into messaging.conversations (id, user_a_id, user_b_id, created_at, updated_at)
            values ({newId}, {pair.UserA}, {pair.UserB}, {now}, {now})
            on conflict (user_a_id, user_b_id) do nothing
            """, ct);

        var conversation = await db.Conversations.AsNoTracking()
            .SingleAsync(c => c.UserAId == pair.UserA && c.UserBId == pair.UserB, ct);
        return (conversation, inserted == 1);
    }

    public Task<IReadOnlyList<Conversation>> ListAsync(Guid userId, ConversationCursor? after, int take, CancellationToken ct)
    {
        // Hai nhánh, mỗi nhánh đủ điều kiện để dùng index một phần idx_conversations_{a,b}_recent (cột đầu = người gọi,
        // WHERE last_message_at IS NOT NULL khớp điều kiện của index), rồi gộp và cắt lại. So sánh hàng (a, b) < (x, y) khớp
        // chiều DESC, DESC của index.
        var query = after is { } c
            ? db.Conversations.FromSql(
                $"""
                select * from (
                    (select * from messaging.conversations
                      where user_a_id = {userId} and last_message_at is not null
                        and (last_message_at, id) < ({c.LastMessageAt}, {c.Id})
                      order by last_message_at desc, id desc limit {take})
                    union all
                    (select * from messaging.conversations
                      where user_b_id = {userId} and last_message_at is not null
                        and (last_message_at, id) < ({c.LastMessageAt}, {c.Id})
                      order by last_message_at desc, id desc limit {take})
                ) x
                """)
            : db.Conversations.FromSql(
                $"""
                select * from (
                    (select * from messaging.conversations
                      where user_a_id = {userId} and last_message_at is not null
                      order by last_message_at desc, id desc limit {take})
                    union all
                    (select * from messaging.conversations
                      where user_b_id = {userId} and last_message_at is not null
                      order by last_message_at desc, id desc limit {take})
                ) x
                """);

        return ToListAsync(query.AsNoTracking().OrderByDescending(x => x.LastMessageAt).ThenByDescending(x => x.Id).Take(take), ct);
    }

    public async Task<long> UnreadTotalAsync(Guid userId, CancellationToken ct) =>
        await db.Database.SqlQuery<long>(
            $"""
            select (
                coalesce((select sum(greatest(seq_counter - user_a_seen_seq, 0)) from messaging.conversations
                           where user_a_id = {userId} and last_message_at is not null), 0)
              + coalesce((select sum(greatest(seq_counter - user_b_seen_seq, 0)) from messaging.conversations
                           where user_b_id = {userId} and last_message_at is not null), 0)
            )::bigint as "Value"
            """).SingleAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, Message>> GetMessagesAsync(
        IReadOnlyCollection<Guid> messageIds, CancellationToken ct)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, Message>();

        return await db.Messages.AsNoTracking().Where(m => messageIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
    }

    public Task<IReadOnlyList<Message>> HistoryAsync(Guid conversationId, long? beforeSeq, int take, CancellationToken ct)
    {
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (beforeSeq is { } before)
            query = query.Where(m => m.Seq < before);

        // Đọc NGƯỢC uq_messages_conv_seq (Index Scan Backward) — không cần index seq DESC thứ hai (Mục 4, chỗ dễ sai số 3).
        return ToListAsync(query.OrderByDescending(m => m.Seq).Take(take), ct);
    }

    public Task<IReadOnlyList<Message>> AfterAsync(Guid conversationId, long afterSeq, int take, CancellationToken ct) =>
        ToListAsync(
            db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.Seq > afterSeq)
                .OrderBy(m => m.Seq)
                .Take(take),
            ct);

    public async Task<ReceiptMarks?> AdvanceMarksAsync(
        Guid conversationId, bool isUserA, ReceiptKind kind, long upToSeq, DateTimeOffset now, CancellationToken ct)
    {
        // Hai hằng, chọn bằng bool — không phải dữ liệu người dùng. Điều kiện WHERE là "mốc sẽ TĂNG": 0 dòng nghĩa là không
        // đổi gì và không phát ReceiptUpdated (Mục 8.2). Với "seen": seen tăng kéo delivered theo (delivered ≥ seen luôn đúng,
        // nên seen không tăng thì delivered cũng không cần tăng tới n).
        var side = isUserA ? "user_a" : "user_b";
        var n = Math.Max(upToSeq, 0);
        var sql = kind == ReceiptKind.Seen
            ? $"""
               update messaging.conversations
                  set {side}_seen_seq = greatest({side}_seen_seq, least(@n, seq_counter)),
                      {side}_delivered_seq = greatest({side}_delivered_seq, least(@n, seq_counter)),
                      updated_at = @now
                where id = @id and {side}_seen_seq < least(@n, seq_counter)
               returning {side}_delivered_seq as "DeliveredSeq", {side}_seen_seq as "SeenSeq"
               """
            : $"""
               update messaging.conversations
                  set {side}_delivered_seq = greatest({side}_delivered_seq, least(@n, seq_counter)),
                      updated_at = @now
                where id = @id and {side}_delivered_seq < least(@n, seq_counter)
               returning {side}_delivered_seq as "DeliveredSeq", {side}_seen_seq as "SeenSeq"
               """;

        var rows = await db.Database.SqlQueryRaw<MarksRow>(
                sql,
                new NpgsqlParameter("n", n),
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("id", conversationId))
            .ToListAsync(ct);

        return rows.Count == 0 ? null : new ReceiptMarks(rows[0].DeliveredSeq, rows[0].SeenSeq);
    }

    public async Task<SendOutcome> SendAsync(SendCommand cmd, CancellationToken ct)
    {
        try
        {
            return await SendOnceAsync(cmd, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ClientMsgIdIndex or SeqIndex,
        })
        {
            // Lưới cuối (Đ-5.4): FOR UPDATE đã tuần tự hóa mọi lượt gửi của hội thoại nên nhánh này không xảy ra — trừ khi một
            // ngày ai đó bỏ khóa dòng. Khi đó 23505 phải thành "đọc lại tin theo clientMsgId và trả nó", không thành 500.
            db.ChangeTracker.Clear();
            var existing = await db.Messages.AsNoTracking()
                .SingleOrDefaultAsync(m => m.ConversationId == cmd.ConversationId && m.ClientMsgId == cmd.ClientMsgId, ct);
            if (existing is null)
                throw;
            return Replay(existing, cmd.Content);
        }
    }

    private async Task<SendOutcome> SendOnceAsync(SendCommand cmd, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 1. Khóa dòng hội thoại: tuần tự hóa mọi lượt gửi của hội thoại này (chat 1-1 — tối đa hai người gửi). Nhờ vậy bước 3
        //    hết race: hai lần gửi lại cùng clientMsgId không thể cùng qua bước 3.
        var locked = await db.Database.SqlQuery<Guid>(
                $"select id as \"Value\" from messaging.conversations where id = {cmd.ConversationId} for update")
            .ToListAsync(ct);
        if (locked.Count == 0)
            return new SendOutcome(SendStatus.NotFound, null);

        // 2. (Tầng 3 — thành viên — đã kiểm TRƯỚC transaction ở ConversationAccess: thành viên của hội thoại cố định, không có
        //    đường nào đổi, nên kiểm trước hay sau khi khóa cho cùng một kết quả.)

        // 3. Đã có clientMsgId này? → trả chính tin đó (Đ-5.5), không cấp seq mới. Transaction rollback khi dispose.
        var existing = await db.Messages.AsNoTracking()
            .SingleOrDefaultAsync(m => m.ConversationId == cmd.ConversationId && m.ClientMsgId == cmd.ClientMsgId, ct);
        if (existing is not null)
            return Replay(existing, cmd.Content);

        // 4. Cấp seq + con trỏ tin cuối + mốc của người gửi (Đ-5.14: gửi tin tự đẩy mốc đã xem của chính mình lên seq vừa cấp,
        //    nên seq_counter − my_seen_seq chỉ đếm tin của người kia). Mọi vế phải dùng giá trị CŨ của seq_counter — Postgres
        //    đánh giá mọi biểu thức SET trên hàng cũ nên "seq_counter + 1" ở ba chỗ là cùng một số.
        var side = cmd.SenderIsUserA ? "user_a" : "user_b";   // hằng, không phải dữ liệu người dùng — giá trị đi bằng tham số
        var sql = $"""
            update messaging.conversations
               set seq_counter = seq_counter + 1,
                   last_message_id = @mid,
                   last_message_at = @now,
                   {side}_seen_seq = seq_counter + 1,
                   {side}_delivered_seq = seq_counter + 1,
                   updated_at = @now
             where id = @cid
            returning seq_counter as "Value"
            """;
        var seqs = await db.Database.SqlQueryRaw<long>(
                sql,
                new NpgsqlParameter("mid", cmd.MessageId),
                new NpgsqlParameter("now", cmd.Now),
                new NpgsqlParameter("cid", cmd.ConversationId))
            .ToListAsync(ct);

        // 5. INSERT tin với seq vừa RETURNING.
        var message = new Message
        {
            Id = cmd.MessageId,
            ConversationId = cmd.ConversationId,
            SenderId = cmd.SenderId,
            Seq = seqs[0],
            Content = cmd.Content,
            ClientMsgId = cmd.ClientMsgId,
            CreatedAt = cmd.Now,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();

        return new SendOutcome(SendStatus.Created, message);
    }

    private static SendOutcome Replay(Message existing, string content) =>
        string.Equals(existing.Content, content, StringComparison.Ordinal)
            ? new SendOutcome(SendStatus.Replayed, existing)
            : new SendOutcome(SendStatus.ClientMsgIdReused, null);

    private static async Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct) =>
        await query.ToListAsync(ct);

    internal sealed record MarksRow(long DeliveredSeq, long SeenSeq);
}
