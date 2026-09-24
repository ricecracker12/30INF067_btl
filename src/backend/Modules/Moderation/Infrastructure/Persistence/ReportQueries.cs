using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.Modules.Moderation.Domain;

namespace SocialApp.Modules.Moderation.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IReportQueries"/> — SQL thô tham số hóa trên kết nối của <see cref="ModerationDbContext"/>, khuôn
/// <see cref="ReportStore"/>. SQL thô vì hàng đợi cần <c>count(*) FILTER</c> theo lý do và <c>array_agg … ORDER BY</c> cho báo cáo đại
/// diện — hai thứ LINQ không dịch được thành một câu.
/// </summary>
internal sealed class ReportQueries(ModerationDbContext db) : IReportQueries
{
    /// <summary>
    /// Một cột đếm cho mỗi lý do, dựng từ <see cref="ReasonCodes.All"/> — CHÍNH mảng mà <c>ck_reports_reason</c> dựng từ, nên thêm lý
    /// do là thêm cột ở đây mà không sửa câu. Giá trị là hằng của code, không phải đầu vào người dùng.
    /// </summary>
    private static readonly string ReasonCounts = string.Join(",\n       ",
        ReasonCodes.All.Select(code => $"count(*) FILTER (WHERE reason_code = '{code}')::int"));

    /// <summary>
    /// Hàng đợi (Đ-6.13): gom theo đối tượng, keyset trên <c>(min(created_at), target_id)</c> ở <c>HAVING</c>.
    /// <c>array_agg(id ORDER BY created_at, id)[1]</c> — báo cáo mở CŨ NHẤT làm đại diện; thiếu <c>ORDER BY</c> thì đại diện ngẫu nhiên
    /// và FE mở chi tiết ra báo cáo khác mỗi lần (cạm bẫy 4). <c>$1</c> null = trang đầu.
    /// </summary>
    private static readonly string QueueSql = $"""
        SELECT target_type, target_id,
               min(created_at)                            AS first_reported_at,
               (array_agg(id ORDER BY created_at, id))[1] AS report_id,
               count(*)::int                              AS report_count,
               {ReasonCounts}
        FROM moderation.reports
        WHERE status = '{ReportStatus.Open}'
        GROUP BY target_type, target_id
        HAVING $1::timestamptz IS NULL OR (min(created_at), target_id) > ($1, $2)
        ORDER BY first_reported_at, target_id
        LIMIT $3
        """;

    /// <summary>
    /// Chi tiết: mọi báo cáo cùng đối tượng với báo cáo <c>$1</c>, trên <c>idx_reports_target</c>. KHÔNG chọn <c>reporter_id</c> — thứ
    /// không đọc lên thì không lọt ra response được (<c>QUE-04</c>).
    /// </summary>
    private const string ThreadSql = """
        SELECT r.target_type, r.target_id, r.id, r.reason_code, r.detail, r.status,
               r.resolver_id, r.resolved_at, r.resolution_note, r.created_at
        FROM moderation.reports anchor
        JOIN moderation.reports r ON r.target_type = anchor.target_type AND r.target_id = anchor.target_id
        WHERE anchor.id = $1
        ORDER BY r.created_at, r.id
        """;

    public async Task<IReadOnlyList<ReportQueueRow>> ListOpenAsync(ReportQueueCursor? after, int take, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(QueueSql, (NpgsqlConnection)db.Database.GetDbConnection());
            cmd.Parameters.Add(new NpgsqlParameter
            {
                Value = after is { } a ? a.FirstReportedAt : DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz,
            });
            cmd.Parameters.Add(new NpgsqlParameter
            {
                Value = after is { } b ? b.TargetId : DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid,
            });
            cmd.Parameters.Add(new NpgsqlParameter { Value = take, NpgsqlDbType = NpgsqlDbType.Integer });

            var rows = new List<ReportQueueRow>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var reasons = new Dictionary<string, int>();
                for (var i = 0; i < ReasonCodes.All.Length; i++)
                {
                    var count = reader.GetInt32(5 + i);
                    if (count > 0)
                        reasons[ReasonCodes.All[i]] = count;
                }

                rows.Add(new ReportQueueRow(
                    reader.GetGuid(3),
                    reader.GetString(0),
                    reader.GetGuid(1),
                    reader.GetInt32(4),
                    reasons,
                    reader.GetFieldValue<DateTimeOffset>(2)));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<ReportThread?> FindThreadAsync(Guid reportId, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(ThreadSql, (NpgsqlConnection)db.Database.GetDbConnection());
            cmd.Parameters.Add(new NpgsqlParameter { Value = reportId, NpgsqlDbType = NpgsqlDbType.Uuid });

            string? targetType = null;
            var targetId = Guid.Empty;
            var reports = new List<ReportRow>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                targetType ??= reader.GetString(0);
                targetId = reader.GetGuid(1);
                reports.Add(new ReportRow(
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetFieldValue<DateTimeOffset>(9)));
            }

            return targetType is null ? null : new ReportThread(targetType, targetId, reports);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
