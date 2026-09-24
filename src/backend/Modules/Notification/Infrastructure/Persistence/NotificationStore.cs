using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Notification.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="INotificationStore"/> — SQL thô tham số hóa trong MỘT transaction của <see cref="NotificationDbContext"/>, khuôn
/// hai bước L-D6 (hướng dẫn khối D): <c>RETURNING</c> của Postgres chỉ thấy giá trị MỚI, mà <c>is_read</c> CŨ mới là thứ quyết định
/// "đợt mới → đếm lại từ 1".
///
/// <list type="number">
/// <item><b>Nhóm đã có:</b> <c>SELECT … FOR UPDATE</c> khóa dòng và đọc <c>is_read</c> cũ → <c>UPDATE … WHERE id</c>. Hai câu, không
/// gộp thành <c>UPDATE … FROM (SELECT … FOR UPDATE)</c> như bản vẽ L-D6: ở câu gộp, bảng đích có thể được quét bằng snapshot cũ TRƯỚC
/// khi truy vấn con chờ khóa, và đúng hay sai lúc đua phụ thuộc vào cách Postgres kiểm lại dòng (EvalPlanQual) theo thứ tự join mà
/// planner chọn. Khóa trước, ghi sau thì câu <c>UPDATE</c> chạy trên dòng mình đang giữ — không có gì để kiểm lại.</item>
/// <item><b>Chưa có:</b> <c>INSERT … ON CONFLICT DO NOTHING RETURNING id</c>. Rỗng nghĩa là người khác vừa chèn và đã <c>COMMIT</c>
/// (câu <c>INSERT</c> chờ ở index unique tới lúc đó) → chạy lại bước 1 ĐÚNG MỘT lần: đó là câu lệnh mới, snapshot mới, thấy dòng.
/// Không chạy lại thì sự kiện của người thứ hai trong lượt đua mất lặng lẽ (cạm bẫy 3, <c>NOTIF-C1</c> ra 19).</item>
/// <item><b>Đếm người:</b> <c>notification_actors</c> (PK cặp) <c>ON CONFLICT DO NOTHING</c>; chỉ khi THẬT SỰ chèn được một dòng, trên
/// nhóm cũ, cùng đợt, mới <c>actor_count + 1</c> (cạm bẫy 2 — tăng mỗi lượt là đếm lượt, <c>NOTIF-04</c> bắt). Dòng mới và đợt mới đã
/// mang sẵn <c>actor_count = 1</c>.</item>
/// </list>
///
/// Mọi mốc thời gian lấy từ <see cref="TimeProvider"/> và gán TRONG SQL: <see cref="NotificationDbContext"/> không tự đóng dấu
/// <c>updated_at</c> (A2), và không đặt nó ở bước 1 thì nhóm có sự kiện mới không nhảy lên đầu danh sách (cạm bẫy 5).
///
/// Không log gì từ <see cref="NotificationUpsert"/>: <c>record</c> tự in mọi thuộc tính, kể cả id người.
/// </summary>
internal sealed class NotificationStore(NotificationDbContext db, TimeProvider clock) : INotificationStore
{
    private const string LockSql = """
        SELECT id, is_read FROM notification.notifications
        WHERE recipient_id = $1 AND group_key = $2
        FOR UPDATE
        """;

    /// <summary>
    /// Dòng đã khóa ở <see cref="LockSql"/>: vế phải của <c>SET</c> đọc giá trị CŨ của dòng, nên <c>CASE WHEN is_read</c> là đúng
    /// <c>is_read</c> vừa đọc. <c>reason_code</c> lấy của sự kiện mới nhất — khôi phục rồi ẩn lại với lý do khác thì hiện lý do mới.
    /// </summary>
    private const string UpdateSql = """
        UPDATE notification.notifications
        SET last_actor_id = $2,
            reason_code   = $3,
            updated_at    = $4,
            actor_count   = CASE WHEN is_read THEN 1 ELSE actor_count END,
            is_read       = false
        WHERE id = $1
        """;

    private const string InsertSql = """
        INSERT INTO notification.notifications
            (id, recipient_id, type, group_key, target_type, target_id, post_id, last_actor_id, actor_count, reason_code, is_read,
             created_at, updated_at)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 1, $9, false, $10, $10)
        ON CONFLICT (recipient_id, group_key) DO NOTHING
        RETURNING id
        """;

    /// <summary>Đợt mới: người của đợt trước không còn tính — đếm lại từ người vừa tới.</summary>
    private const string ResetActorsSql = "DELETE FROM notification.notification_actors WHERE notification_id = $1";

    private const string AddActorSql = """
        INSERT INTO notification.notification_actors (notification_id, actor_id) VALUES ($1, $2)
        ON CONFLICT DO NOTHING
        """;

    private const string CountActorSql =
        "UPDATE notification.notifications SET actor_count = actor_count + 1 WHERE id = $1";

    public async Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upsert);
        EnsureValid(upsert);

        // Đ-6.16: không tự báo mình. Một chỗ canh cho mọi handler.
        if (upsert.ActorId == upsert.RecipientId)
            return UpsertResult.Skipped;

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = new GroupTransaction((NpgsqlTransaction)tx.GetDbTransaction());

        Guid id;
        UpsertResult result;
        bool countedAlready;   // dòng mới / đợt mới đã mang actor_count = 1 cho chính người này

        var existing = await batch.LockAsync(upsert, ct);
        if (existing is null && await batch.InsertAsync(upsert, now, ct) is { } created)
        {
            (id, result, countedAlready) = (created, UpsertResult.Created, true);
        }
        else
        {
            // Nhánh "người khác vừa chèn": chạy lại bước 1 MỘT lần. Vẫn không thấy là dòng vừa bị xóa giữa hai câu (chỉ job dọn của
            // GĐ8 làm vậy) — ném để bus ghi log, không bịa một kết quả.
            var locked = existing ?? await batch.LockAsync(upsert, ct)
                ?? throw new InvalidOperationException("Nhóm thông báo không chèn được mà cũng không khóa được sau khi chèn trùng.");

            await batch.ExecuteAsync(UpdateSql, ct,
                Uuid(locked.Id), Uuid(upsert.ActorId), Varchar(upsert.ReasonCode), Timestamp(now));
            if (locked.WasRead)
                await batch.ExecuteAsync(ResetActorsSql, ct, Uuid(locked.Id));

            (id, result, countedAlready) = (locked.Id, UpsertResult.Updated, locked.WasRead);
        }

        // Moderation không có người (last_actor_id NULL, 0 dòng notification_actors) — không lộ ai kiểm duyệt.
        if (upsert.ActorId is { } actor)
        {
            var added = await batch.ExecuteAsync(AddActorSql, ct, Uuid(id), Uuid(actor));
            if (added == 1 && !countedAlready)
                await batch.ExecuteAsync(CountActorSql, ct, Uuid(id));
        }

        // Không truyền ct: tới đây mọi thứ đã ghi — hủy COMMIT lúc này chỉ để sự kiện mất một nửa.
        await tx.CommitAsync(CancellationToken.None);
        return result;
    }

    /// <summary>
    /// <c>moderation</c> ⇔ không có người. Handler viết nhầm (ví dụ truyền id Moderator) phải nổ ở đây, trước mọi I/O — không phải ghi
    /// id đó vào <c>last_actor_id</c> rồi để danh sách hydrate ra tên người kiểm duyệt (B.10 #7).
    /// </summary>
    private static void EnsureValid(NotificationUpsert upsert)
    {
        var moderation = upsert.Type == NotificationTypes.Moderation;
        if (moderation != (upsert.ActorId is null))
            throw new ArgumentException(
                moderation
                    ? "Thông báo kiểm duyệt không được mang người thao tác."
                    : "Thông báo không phải kiểm duyệt phải có người thao tác.",
                nameof(upsert));

        if (!moderation && upsert.ReasonCode is not null)
            throw new ArgumentException("Chỉ thông báo kiểm duyệt mới mang mã lý do.", nameof(upsert));
    }

    private sealed record LockedGroup(Guid Id, bool WasRead);

    /// <summary>
    /// Các câu lệnh trên CHÍNH transaction của một lần upsert. Transaction nằm ở trường, không đi qua tham số: phương thức nhận
    /// <c>DbTransaction</c> là dấu hiệu nhận ra hợp đồng ghi XUYÊN MODULE (Đ-6.3, <c>WriteContractTests</c>) — đây là đường ghi trong
    /// module, không phải hợp đồng thứ ba.
    /// </summary>
    private sealed class GroupTransaction(NpgsqlTransaction tx)
    {
        public async Task<LockedGroup?> LockAsync(NotificationUpsert upsert, CancellationToken ct)
        {
            await using var cmd = Command(LockSql, Uuid(upsert.RecipientId), Varchar(upsert.GroupKey));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? new LockedGroup(reader.GetGuid(0), reader.GetBoolean(1)) : null;
        }

        public async Task<Guid?> InsertAsync(NotificationUpsert upsert, DateTimeOffset now, CancellationToken ct)
        {
            await using var cmd = Command(InsertSql,
                Uuid(Uuid7.New()),
                Uuid(upsert.RecipientId),
                Varchar(upsert.Type),
                Varchar(upsert.GroupKey),
                Varchar(upsert.TargetType),
                Uuid(upsert.TargetId),
                Uuid(upsert.PostId),
                Uuid(upsert.ActorId),
                Varchar(upsert.ReasonCode),
                Timestamp(now));
            return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
        }

        public async Task<int> ExecuteAsync(string sql, CancellationToken ct, params NpgsqlParameter[] parameters)
        {
            await using var cmd = Command(sql, parameters);
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        private NpgsqlCommand Command(string sql, params NpgsqlParameter[] parameters)
        {
            var cmd = new NpgsqlCommand(sql, tx.Connection, tx);
            cmd.Parameters.AddRange(parameters);
            return cmd;
        }
    }

    private static NpgsqlParameter Uuid(Guid? value) =>
        new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid };

    private static NpgsqlParameter Varchar(string? value) =>
        new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar };

    private static NpgsqlParameter Timestamp(DateTimeOffset value) =>
        new() { Value = value, NpgsqlDbType = NpgsqlDbType.TimestampTz };
}
