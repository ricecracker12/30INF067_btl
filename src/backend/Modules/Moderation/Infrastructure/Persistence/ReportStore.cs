using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.Modules.Moderation.Domain;
using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Moderation.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IReportStore"/> — SQL thô tham số hóa trên kết nối của <see cref="ModerationDbContext"/>, khuôn
/// <c>SqlAuditTrail</c>.
///
/// <b>Một báo cáo mở mỗi (người, đối tượng)</b> do index một phần <c>uq_reports_open_per_reporter</c> giữ, không do lần đọc trước khi
/// ghi: <c>INSERT … ON CONFLICT … WHERE status = 'open' DO NOTHING RETURNING</c> đúng câu A1 đã khóa hình dạng
/// (<c>Report_on_conflict_voi_index_mot_phan_khong_chen_trung</c>). Thiếu vế <c>WHERE</c> thì Postgres không suy ra được index
/// một phần → <c>42P10</c> lúc CHẠY (cạm bẫy 1). Mười lượt song song: một lượt chèn, chín lượt chờ lượt đó <c>COMMIT</c> rồi
/// <c>DO NOTHING</c> — câu <c>SELECT</c> sau đó là câu lệnh mới, snapshot mới, thấy dòng vừa chèn (READ COMMITTED).
///
/// Vòng lặp hai lượt cho đúng một khe hẹp: giữa <c>DO NOTHING</c> và <c>SELECT</c>, báo cáo mở kia vừa bị Moderator đóng (D7) →
/// <c>SELECT</c> rỗng, và lúc đó chèn lại là đúng (báo lại sau khi đã xử lý → báo cáo mới). Hết hai lượt vẫn rỗng là điều không
/// thể xảy ra theo luật của bảng — ném, không trả một biên nhận bịa.
///
/// Không mở transaction: mỗi câu tự <c>COMMIT</c>, và không có ghi thứ hai nào phải cùng số phận (không audit — xem service).
/// </summary>
internal sealed class ReportStore(ModerationDbContext db) : IReportStore
{
    private static readonly string InsertSql = $"""
        INSERT INTO moderation.reports (id, reporter_id, target_type, target_id, reason_code, detail, status, created_at, updated_at)
        VALUES ($1, $2, $3, $4, $5, $6, '{ReportStatus.Open}', $7, $7)
        ON CONFLICT (reporter_id, target_type, target_id) WHERE status = '{ReportStatus.Open}' DO NOTHING
        RETURNING id, created_at
        """;

    private static readonly string SelectOpenSql = $"""
        SELECT id, created_at FROM moderation.reports
        WHERE reporter_id = $1 AND target_type = $2 AND target_id = $3 AND status = '{ReportStatus.Open}'
        """;

    public async Task<(ReportReceipt Receipt, bool Created)> CreateOrGetOpenAsync(
        Guid reporterId, string targetType, Guid targetId, string reasonCode, string? detail, DateTimeOffset now,
        CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using (var insert = new NpgsqlCommand(InsertSql, connection))
                {
                    insert.Parameters.Add(new NpgsqlParameter { Value = Uuid7.New(), NpgsqlDbType = NpgsqlDbType.Uuid });
                    AddTargetKey(insert, reporterId, targetType, targetId);
                    insert.Parameters.Add(new NpgsqlParameter { Value = reasonCode, NpgsqlDbType = NpgsqlDbType.Varchar });
                    insert.Parameters.Add(new NpgsqlParameter { Value = (object?)detail ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
                    insert.Parameters.Add(new NpgsqlParameter { Value = now, NpgsqlDbType = NpgsqlDbType.TimestampTz });

                    if (await ReadReceiptAsync(insert, ct) is { } created)
                        return (created, true);
                }

                await using (var select = new NpgsqlCommand(SelectOpenSql, connection))
                {
                    AddTargetKey(select, reporterId, targetType, targetId);
                    if (await ReadReceiptAsync(select, ct) is { } existing)
                        return (existing, false);
                }
            }

            throw new InvalidOperationException("Không chèn được báo cáo mà cũng không thấy báo cáo mở nào sau hai lượt.");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Ba tham số đầu (<c>$2..$4</c> của câu chèn, <c>$1..$3</c> của câu đọc) — cùng thứ tự ở cả hai câu.</summary>
    private static void AddTargetKey(NpgsqlCommand cmd, Guid reporterId, string targetType, Guid targetId)
    {
        cmd.Parameters.Add(new NpgsqlParameter { Value = reporterId, NpgsqlDbType = NpgsqlDbType.Uuid });
        cmd.Parameters.Add(new NpgsqlParameter { Value = targetType, NpgsqlDbType = NpgsqlDbType.Varchar });
        cmd.Parameters.Add(new NpgsqlParameter { Value = targetId, NpgsqlDbType = NpgsqlDbType.Uuid });
    }

    private static async Task<ReportReceipt?> ReadReceiptAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new ReportReceipt(reader.GetGuid(0), ReportStatus.Open, reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }
}
