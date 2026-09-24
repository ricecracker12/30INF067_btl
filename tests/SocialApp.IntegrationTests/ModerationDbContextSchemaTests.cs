using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Moderation.DependencyInjection;
using SocialApp.Modules.Moderation.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Bản Moderation của <see cref="SocialGraphDbContextSchemaTests"/> (A1, GĐ6 khối A). Mỗi khẳng định ứng với một luật mà nếu
/// cấu hình sai thì không có lỗi nào khác báo: bảng lịch sử đúng schema, trigger append-only của audit (<c>AUD-02</c>, Đ-6.15),
/// CHECK và index một phần của báo cáo (Đ-6.12, giai-doan-6.md Mục 4).
///
/// Mỗi test một database mới (<see cref="PostgresFixture.CreateDatabaseAsync"/>) — các test ở đây sửa dữ liệu.
///
/// <see cref="DisposeAsync"/> TRẢ kết nối của database đó về Postgres ngay khi ca xong (<c>ClearPool</c>): pool Npgsql giữ kết
/// nối rảnh tới 300 giây, và cả collection dùng chung một container <c>max_connections = 100</c>. Không trả thì mười một ca ở
/// đây để lại chừng hai chục kết nối rảnh — đủ đẩy bộ test sát trần sang <c>53300 too many clients already</c> ở các lớp chạy
/// sau (đo 2026-09-23 khi thi công A1: 59 ca Auth đỏ đúng lỗi đó).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ModerationDbContextSchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Guid Actor = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Reporter = Guid.Parse("01900000-0000-7000-8000-0000000000b1");
    private static readonly Guid Target = Guid.Parse("01900000-0000-7000-8000-0000000000c1");

    private ServiceProvider? _services;
    private string? _connectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        if (_connectionString is not null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(conn);
        }
    }

    private async Task<(ServiceProvider Services, string ConnectionString)> MigratedAsync(PostgresFixture postgres)
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection()
            .AddModerationModule(_connectionString)
            .BuildServiceProvider();

        await _services.MigrateModerationModuleAsync();
        return (_services, _connectionString);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection conn, string sql, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static Task InsertAuditAsync(NpgsqlConnection conn) => ExecuteAsync(conn, $$"""
        insert into moderation.audit_logs (actor_id, action, target_type, target_id, metadata, ip)
        values ('{{Actor}}', 'user.lock', 'user', '{{Target}}', '{"fromRole":"USER"}', '203.0.113.7')
        """);

    private static async Task<long> CountAuditAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("select count(*) from moderation.audit_logs", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task InsertReportAsync(NpgsqlConnection conn, string reason = "spam", string? detail = null,
        string status = "open", bool decided = false) => ExecuteAsync(conn, $"""
        insert into moderation.reports (id, reporter_id, target_type, target_id, reason_code, detail, status, resolver_id, resolved_at)
        values (gen_random_uuid(), '{Reporter}', 'post', '{Target}', '{reason}',
                {(detail is null ? "null" : $"'{detail}'")}, '{status}',
                {(decided ? $"'{Actor}'" : "null")}, {(decided ? "now()" : "null")})
        """);

    /// <summary>Bảng lịch sử của Moderation nằm trong schema <c>moderation</c>.</summary>
    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_moderation()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();

        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value"
                from information_schema.tables
                where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([ModerationDbContext.Schema], schemas);
    }

    /// <summary>
    /// AUD-02 (Đ-6.15): <c>UPDATE</c>, <c>DELETE</c>, <c>TRUNCATE</c> trên <c>audit_logs</c> đều bị Postgres từ chối — so
    /// SqlState <c>P0001</c> (RAISE của trigger), không so câu chữ. Dòng audit vẫn còn sau cả ba lần.
    /// </summary>
    [Fact]
    public async Task AUD_02_update_delete_truncate_audit_deu_bi_tu_choi()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);
        await InsertAuditAsync(conn);

        foreach (var sql in new[]
                 {
                     "update moderation.audit_logs set action = 'user.unlock'",
                     "delete from moderation.audit_logs",
                     "truncate moderation.audit_logs",
                 })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(conn, sql));
            Assert.Equal(PostgresErrorCodes.RaiseException, ex.SqlState);   // P0001
        }

        Assert.Equal(1, await CountAuditAsync(conn));
    }

    /// <summary>
    /// AUD-02, cửa purge của GĐ8: <c>DELETE</c> với <c>SET LOCAL socialapp.audit_purge = 'on'</c> TRONG transaction thì được.
    /// <c>SET LOCAL</c> ngoài transaction không có tác dụng — ca này mở transaction tường minh, nếu không nó đỏ vì lý do sai.
    /// </summary>
    [Fact]
    public async Task AUD_02_delete_voi_co_purge_trong_transaction_thi_duoc()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);
        await InsertAuditAsync(conn);

        await using (var tx = await conn.BeginTransactionAsync())
        {
            await ExecuteAsync(conn, "set local socialapp.audit_purge = 'on'", tx);
            Assert.Equal(1, await ExecuteAsync(conn, "delete from moderation.audit_logs", tx));
            await tx.CommitAsync();
        }

        Assert.Equal(0, await CountAuditAsync(conn));
    }

    /// <summary>AUD-02b: cửa purge chỉ mở cho <c>DELETE</c> — <c>UPDATE</c> với cờ bật vẫn bị từ chối.</summary>
    [Fact]
    public async Task AUD_02b_update_voi_co_purge_van_bi_tu_choi()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);
        await InsertAuditAsync(conn);

        await using var tx = await conn.BeginTransactionAsync();
        await ExecuteAsync(conn, "set local socialapp.audit_purge = 'on'", tx);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(conn, "update moderation.audit_logs set action = 'user.unlock'", tx));
        Assert.Equal(PostgresErrorCodes.RaiseException, ex.SqlState);
    }

    /// <summary>
    /// Bốn CHECK của <c>reports</c> chặn thật: lý do ngoài tập, <c>other</c> thiếu <c>detail</c>, <c>resolved</c> không người quyết,
    /// <c>open</c> mà đã có người quyết. So cả tên constraint để biết CHECK nào bắt.
    /// </summary>
    [Theory]
    [InlineData("abuse", null, "open", false, "ck_reports_reason")]
    [InlineData("other", null, "open", false, "ck_reports_other_detail")]
    [InlineData("spam", null, "resolved", false, "ck_reports_decided")]
    [InlineData("spam", null, "open", true, "ck_reports_decided")]
    [InlineData("spam", null, "closed", true, "ck_reports_status")]
    public async Task Report_CHECK_chan_that(string reason, string? detail, string status, bool decided, string constraint)
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => InsertReportAsync(conn, reason, detail, status, decided));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    /// <summary>
    /// Đ-6.12: một báo cáo MỞ mỗi (người báo, đối tượng) — dòng thứ hai bị <c>uq_reports_open_per_reporter</c> chặn. Đóng dòng
    /// đầu rồi báo lại thì được: index một phần không chặn dòng đã xử lý (REP-06 ở tầng DB).
    /// </summary>
    [Fact]
    public async Task Report_mot_bao_cao_mo_moi_nguoi_moi_doi_tuong()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        await InsertReportAsync(conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertReportAsync(conn));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("uq_reports_open_per_reporter", ex.ConstraintName);

        await ExecuteAsync(conn,
            $"update moderation.reports set status = 'dismissed', resolver_id = '{Actor}', resolved_at = now()");
        await InsertReportAsync(conn);   // báo lại sau khi báo cáo cũ đã đóng
    }

    /// <summary>
    /// ON CONFLICT mà D6 sẽ viết chạy được trên index một phần — nêu đúng vế WHERE (Mục 4 chỗ dễ sai 1). Thiếu vế đó là 42P10
    /// lúc chạy; test này khóa hình dạng câu từ tầng dữ liệu để D6 không phải đoán.
    /// </summary>
    [Fact]
    public async Task Report_on_conflict_voi_index_mot_phan_khong_chen_trung()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        const string upsert = """
            insert into moderation.reports (id, reporter_id, target_type, target_id, reason_code)
            values (gen_random_uuid(), '01900000-0000-7000-8000-0000000000b1', 'post',
                    '01900000-0000-7000-8000-0000000000c1', 'spam')
            on conflict (reporter_id, target_type, target_id) where status = 'open' do nothing
            """;

        Assert.Equal(1, await ExecuteAsync(conn, upsert));
        Assert.Equal(0, await ExecuteAsync(conn, upsert));
    }
}
