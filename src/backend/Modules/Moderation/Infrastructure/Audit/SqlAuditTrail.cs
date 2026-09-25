using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SocialApp.SharedKernel.Audit;

namespace SocialApp.Modules.Moderation.Infrastructure.Audit;

/// <summary>
/// Hiện thực <see cref="IAuditTrail"/> (Đ-6.3, Đ-6.15): một câu <c>INSERT</c> tham số hóa vào <c>moderation.audit_logs</c>.
///
/// <b>Có <c>tx</c> → chạy <see cref="NpgsqlCommand"/> trên CHÍNH <c>tx.Connection</c> + <c>tx</c></b>. Không <c>DbContext</c> thứ hai,
/// không <c>SaveChanges</c>, không <c>UseTransaction</c>, không <c>new NpgsqlConnection</c>: mọi đường đó ghi trên kết nối khác, nên
/// dòng audit SỐNG SÓT khi thao tác rollback — đúng lỗi R6-06, và chạy "tốt" mọi lúc trừ lúc có lỗi. Test
/// <c>AuditTrailTests.TX_01_*</c> đỏ nếu ai đổi sang một trong các đường đó.
///
/// <b>Không <c>tx</c></b> → kết nối RIÊNG, <c>new NpgsqlConnection</c> với CHÍNH chuỗi kết nối của <see cref="ModerationDbContext"/>: Npgsql
/// gom pool theo chuỗi kết nối, nên đây là một kết nối nữa từ CÙNG pool (PERF-03 GĐ4), không phải pool thứ hai — không dựng
/// <c>NpgsqlDataSource</c> riêng (L-C1, chốt 2026-09-23). Kết nối riêng là cả ý nghĩa của nhánh này: <c>access.denied</c> ghi lúc
/// không có thao tác nào để chung số phận, nên nó phải sống sót dù scope đang mở transaction nào rồi rollback.
/// <i>Sửa 2026-09-25 (việc treo của D7c):</i> bản đầu dùng kết nối scoped của context — đang có transaction mở trên context đó thì
/// Postgres cho câu INSERT vào luôn transaction ấy (Npgsql không bắt gán <c>cmd.Transaction</c>), trái lời hứa của
/// <see cref="IAuditTrail"/>. Test <c>AuditTrailTests.Khong_tx_song_sot_khi_transaction_cua_scope_rollback</c> canh.
///
/// Không bao giờ log <see cref="AuditEntry"/>: <c>record</c> tự in mọi thuộc tính, kể cả ghi chú của Moderator (B.10 tự rà #5).
/// </summary>
internal sealed class SqlAuditTrail(ModerationDbContext db, IHttpContextAccessor http, TimeProvider clock) : IAuditTrail
{
    private const string InsertSql = """
        INSERT INTO moderation.audit_logs (actor_id, action, target_type, target_id, metadata, ip, created_at)
        VALUES ($1, $2, $3, $4, $5, $6, $7)
        """;

    public async Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (tx is not null)
        {
            // Transaction đã commit/rollback thì Connection là null — ném rõ ràng thay vì NullReference ở dòng dưới.
            var connection = tx.Connection as NpgsqlConnection
                ?? throw new InvalidOperationException("Transaction của người gọi đã đóng hoặc không phải kết nối Npgsql.");

            await using var cmd = new NpgsqlCommand(InsertSql, connection, (NpgsqlTransaction)tx);
            AddParameters(cmd, entry);
            await cmd.ExecuteNonQueryAsync(ct);
            return;
        }

        // Kết nối riêng từ cùng pool — xem phần đầu lớp. KHÔNG dùng db.Database.GetDbConnection(): đó là kết nối scoped, có thể đang
        // giữ transaction của người gọi.
        await using var own = new NpgsqlConnection(db.Database.GetConnectionString());
        await own.OpenAsync(ct);
        await using var ownCmd = new NpgsqlCommand(InsertSql, own);
        AddParameters(ownCmd, entry);
        await ownCmd.ExecuteNonQueryAsync(ct);
    }

    private void AddParameters(NpgsqlCommand cmd, AuditEntry entry)
    {
        cmd.Parameters.Add(new NpgsqlParameter { Value = entry.ActorId, NpgsqlDbType = NpgsqlDbType.Uuid });
        cmd.Parameters.Add(new NpgsqlParameter { Value = entry.Action, NpgsqlDbType = NpgsqlDbType.Varchar });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.TargetType ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.TargetId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        cmd.Parameters.Add(new NpgsqlParameter
        {
            Value = entry.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(entry.Metadata),
            NpgsqlDbType = NpgsqlDbType.Jsonb,
        });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)ClientIp() ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Inet });
        cmd.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow(), NpgsqlDbType = NpgsqlDbType.TimestampTz });
    }

    /// <summary>
    /// IP của request hiện tại, đọc SAU <c>ForwardedHeaders</c> (đầu pipeline, GĐ1) nên là IP người dùng, không phải của apache.
    /// IPv4 bọc trong IPv6 (<c>::ffff:1.2.3.4</c> — Kestrel dual-stack) đổi về IPv4 để người đọc nhật ký lọc được. Không có
    /// request (job nền) → null.
    /// </summary>
    private IPAddress? ClientIp()
    {
        var ip = http.HttpContext?.Connection.RemoteIpAddress;
        return ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;
    }
}
