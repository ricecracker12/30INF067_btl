using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Moderation.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IModerationDecisionStore"/> — mốc 3 của GĐ6: ẩn bài (bảng của Content) + đóng báo cáo + audit trong MỘT transaction
/// Postgres của <see cref="ModerationDbContext"/>. <see cref="IModerationTargets"/> và <see cref="IAuditTrail"/> nhận CHÍNH
/// <see cref="DbTransaction"/> đó (Đ-6.3) — không kết nối thứ hai nào (R6-06).
///
/// Mọi nhánh dừng giữa chừng <c>return</c> trước <c>CommitAsync</c>: <c>await using</c> dispose transaction chưa commit = ROLLBACK. Lỗi
/// ở bất kỳ bước nào (audit hỏng, provider ném) chảy lên thành 500 cũng đi đúng đường đó (<c>TX-01</c>, <c>TX-02</c>).
///
/// Không log <c>note</c> (B.10 #5). Không <c>Publish</c> ở đây — event là việc của service, SAU khi hàm này trả về (luật 5).
/// </summary>
internal sealed class ModerationDecisionStore(ModerationDbContext db, IModerationTargets targets, IAuditTrail audit)
    : IModerationDecisionStore
{
    private const string FindSql =
        "SELECT target_type, target_id, reason_code FROM moderation.reports WHERE id = $1";

    /// <summary>
    /// Khóa dòng VÀ đọc trạng thái trong CÙNG câu (cạm bẫy 4): đọc trước rồi mới khóa thì hai Moderator cùng thấy <c>open</c>. Người
    /// đến sau chờ ở đây tới khi người trước <c>COMMIT</c>, rồi đọc được <c>resolved</c> → 409 (<c>MOD-C1</c>).
    /// </summary>
    private const string LockSql = "SELECT status FROM moderation.reports WHERE id = $1 FOR UPDATE";

    /// <summary>
    /// Đóng MỌI báo cáo mở của đối tượng (Đ-6.13 bước 3) — mười người báo một bài, một quyết định đóng cả mười, trên
    /// <c>idx_reports_target</c>. Cùng người, cùng mốc, cùng ghi chú: đó là khóa gom "một lần quyết" của lịch sử (L-D15, D7b).
    /// </summary>
    private static readonly string CloseSql = $"""
        UPDATE moderation.reports
        SET status = $1, resolver_id = $2, resolved_at = $3, resolution_note = $4, updated_at = $3
        WHERE target_type = $5 AND target_id = $6 AND status = '{ReportStatus.Open}'
        RETURNING id
        """;

    public async Task<ReportHead?> FindReportAsync(Guid reportId, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(FindSql, (NpgsqlConnection)db.Database.GetDbConnection());
            cmd.Parameters.Add(new NpgsqlParameter { Value = reportId, NpgsqlDbType = NpgsqlDbType.Uuid });
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                ? new ReportHead(reader.GetString(0), reader.GetGuid(1), reader.GetString(2))
                : null;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<DecisionOutcome> DecideAsync(ReportDecisionCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var dbTx = (NpgsqlTransaction)tx.GetDbTransaction();

        // 1. Khóa báo cáo được mở; không còn open → dừng (US-019 AC-04).
        await using (var lockCmd = new NpgsqlCommand(LockSql, dbTx.Connection, dbTx))
        {
            lockCmd.Parameters.Add(new NpgsqlParameter { Value = command.ReportId, NpgsqlDbType = NpgsqlDbType.Uuid });
            if (await lockCmd.ExecuteScalarAsync(ct) is not string status || status != ReportStatus.Open)
                return DecisionOutcome.Stopped(DecisionStatus.AlreadyDecided);
        }

        // 2. hide: module chủ tự UPDATE bảng của mình, TRÊN tx này. AlreadyHidden (báo cáo thứ hai cho bài đã ẩn) → đi tiếp.
        HideOutcome? hide = null;
        if (command.Decision == ReportDecision.Hide)
        {
            hide = await targets.HideAsync(dbTx, command.Target, command.ReasonCode, ct);
            if (hide == HideOutcome.NotFound)
                return DecisionOutcome.Stopped(DecisionStatus.TargetGone);
        }

        // 3. Đóng mọi báo cáo mở của đối tượng.
        var closed = new List<Guid>();
        await using (var close = new NpgsqlCommand(CloseSql, dbTx.Connection, dbTx))
        {
            close.Parameters.Add(new NpgsqlParameter
            {
                Value = DecisionRules.ReportStatusFor(command.Decision), NpgsqlDbType = NpgsqlDbType.Varchar,
            });
            close.Parameters.Add(new NpgsqlParameter { Value = command.ActorId, NpgsqlDbType = NpgsqlDbType.Uuid });
            close.Parameters.Add(new NpgsqlParameter { Value = command.Now, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            close.Parameters.Add(new NpgsqlParameter
            {
                Value = (object?)command.Note ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar,
            });
            close.Parameters.Add(new NpgsqlParameter { Value = command.TargetType, NpgsqlDbType = NpgsqlDbType.Varchar });
            close.Parameters.Add(new NpgsqlParameter { Value = command.Target.Id, NpgsqlDbType = NpgsqlDbType.Uuid });

            await using var reader = await close.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                closed.Add(reader.GetGuid(0));
        }

        closed.Sort();

        // 4. Audit trong CÙNG transaction. Không body, không detail của người báo (AUD-01) — chỉ id, mã lý do, ghi chú của Moderator.
        await audit.AppendAsync(dbTx, new AuditEntry(
            command.ActorId, DecisionRules.AuditActionFor(command.Decision), command.TargetType, command.Target.Id,
            new Dictionary<string, object?>
            {
                ["reportIds"] = closed,
                ["reasonCode"] = command.ReasonCode,
                ["note"] = command.Note,
            }), ct);

        // Không truyền ct: tới đây mọi thứ đã ghi — hủy COMMIT lúc này chỉ để client đoán mò kết quả.
        await tx.CommitAsync(CancellationToken.None);
        return new DecisionOutcome(DecisionStatus.Decided, closed, hide);
    }

    public async Task<RestoreOutcome> RestoreAsync(
        ModerationTarget target, string targetType, string? note, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var dbTx = tx.GetDbTransaction();

        var outcome = await targets.RestoreAsync(dbTx, target, ct);
        if (outcome != RestoreOutcome.Restored)
            return outcome;

        await audit.AppendAsync(dbTx, new AuditEntry(
            actorId, AuditActions.ContentRestore, targetType, target.Id,
            new Dictionary<string, object?> { ["note"] = note }), ct);

        await tx.CommitAsync(CancellationToken.None);
        return RestoreOutcome.Restored;
    }
}
