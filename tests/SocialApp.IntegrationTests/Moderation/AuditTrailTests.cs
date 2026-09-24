using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;
using SocialApp.Modules.Moderation.DependencyInjection;
using SocialApp.Modules.Moderation.Infrastructure;
using SocialApp.SharedKernel.Audit;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// C1 (GĐ6) — <see cref="IAuditTrail"/> trên Postgres thật, bản HẠ TẦNG của <c>TX-01</c> và <c>AUD-01</c> (L-C2 của hướng dẫn khối
/// A+C): gọi thẳng hợp đồng, không qua endpoint. Bản đầy đủ qua <c>PATCH /reports</c> là của D7.
///
/// Ca quan trọng nhất là <see cref="TX_01_thao_tac_Identity_nem_sau_audit_thi_ca_hai_rollback"/>: nó là bằng chứng transaction
/// xuyên module (identity + moderation) là MỘT. Hiện thực mở kết nối riêng dù có <c>tx</c> thì dòng audit sống sót sau rollback và
/// ca này đỏ — đột biến bắt buộc của B5.
///
/// Mỗi test một database mới; <see cref="DisposeAsync"/> trả kết nối ngay (xem <see cref="ModerationDbContextSchemaTests"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditTrailTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T10:07:30Z");
    private static readonly Guid Admin = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Target = Guid.Parse("01900000-0000-7000-8000-0000000000c1");

    private readonly FakeHttpContextAccessor _http = new();
    private ServiceProvider _services = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        // Đăng ký TRƯỚC module: AddModerationModule dùng TryAdd cho đồng hồ và AddHttpContextAccessor (cũng TryAdd).
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedTime(Now))
            .AddSingleton<IHttpContextAccessor>(_http)
            .AddIdentityModule(_connectionString)
            .AddModerationModule(_connectionString)
            .BuildServiceProvider();

        await _services.MigrateIdentityModuleAsync();      // migrate + seed vai trò (users.role_id cần roles)
        await _services.MigrateModerationModuleAsync();

        await ExecuteAsync($"""
            insert into identity.users (user_id, email, password_hash, role_id)
            values ('{Target}', 'c1@test.local', 'khong-phai-hash-that', 1)
            """);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_connectionString);
        NpgsqlConnection.ClearPool(conn);
    }

    /// <summary>
    /// TX-01 (bản hạ tầng): khóa tài khoản (UPDATE identity.users) + ghi audit TRÊN CÙNG transaction, rồi lỗi trước COMMIT → cả
    /// hai biến mất. Đây là hình dạng D3 (khóa) và D7 (ẩn + đóng báo cáo + audit) sẽ dùng.
    /// </summary>
    [Fact]
    public async Task TX_01_thao_tac_Identity_nem_sau_audit_thi_ca_hai_rollback()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var scope = _services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

            await using var tx = await identity.Database.BeginTransactionAsync();
            await identity.Database.ExecuteSqlAsync($"update identity.users set status = 'disabled' where user_id = {Target}");
            await audit.AppendAsync(tx.GetDbTransaction(), LockEntry());

            throw new InvalidOperationException("lỗi giả sau khi đã ghi audit, trước COMMIT");
        });

        Assert.Equal("active", await ScalarAsync($"select status from identity.users where user_id = '{Target}'"));
        Assert.Equal("0", await ScalarAsync("select count(*)::text from moderation.audit_logs"));
    }

    /// <summary>TX-01, vế commit: thao tác và dòng audit cùng có mặt, đúng người thao tác, hành động, đối tượng.</summary>
    [Fact]
    public async Task TX_01_commit_thi_thao_tac_va_audit_cung_co()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

            await using var tx = await identity.Database.BeginTransactionAsync();
            await identity.Database.ExecuteSqlAsync($"update identity.users set status = 'disabled' where user_id = {Target}");
            await audit.AppendAsync(tx.GetDbTransaction(), LockEntry());
            await tx.CommitAsync();
        }

        Assert.Equal("disabled", await ScalarAsync($"select status from identity.users where user_id = '{Target}'"));
        Assert.Equal(
            $"{Admin}|user.lock|user|{Target}",
            await ScalarAsync("select actor_id || '|' || action || '|' || target_type || '|' || target_id from moderation.audit_logs"));
    }

    /// <summary>
    /// AUD-01 (bản hạ tầng), đường <c>tx == null</c> (Đ-6.15 — audit khi bị từ chối): IP lấy từ request hiện tại, IPv4 bọc IPv6 đổi
    /// về IPv4; metadata đọc lại đúng khóa; <c>created_at</c> từ đồng hồ của app, không từ <c>now()</c> của DB.
    /// Bản đầy đủ (không dòng nào chứa nội dung bài bị ẩn) là của D7.
    /// </summary>
    [Fact]
    public async Task AUD_01_khong_tx_ghi_ip_metadata_va_dong_ho_cua_app()
    {
        _http.HttpContext = new DefaultHttpContext();
        _http.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.7");
        var reportIds = new[] { Guid.Parse("01900000-0000-7000-8000-0000000000d1"), Guid.Parse("01900000-0000-7000-8000-0000000000d2") };

        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditTrail>().AppendAsync(null, new AuditEntry(
                Admin, AuditActions.ReportHide, "post", Target,
                new Dictionary<string, object?> { ["reportIds"] = reportIds, ["reasonCode"] = "spam", ["note"] = "đã xem" }));
        }

        Assert.Equal(
            "203.0.113.7|2|spam|đã xem|2026-09-23T10:07:30Z",
            await ScalarAsync("""
                select host(ip) || '|' || jsonb_array_length(metadata->'reportIds') || '|' || (metadata->>'reasonCode')
                       || '|' || (metadata->>'note') || '|' || to_char(created_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
                from moderation.audit_logs
                """));
    }

    /// <summary>Không có request (job nền) → <c>ip</c> NULL, không lỗi; không metadata → <c>metadata</c> NULL.</summary>
    [Fact]
    public async Task Khong_request_thi_ip_null_va_khong_metadata_thi_null()
    {
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAuditTrail>()
                .AppendAsync(null, new AuditEntry(Admin, AuditActions.AccessDenied, "endpoint", null));

        Assert.Equal("t|t", await ScalarAsync(
            "select (ip is null)::text::char || '|' || (metadata is null)::text::char from moderation.audit_logs"));
    }

    /// <summary>Transaction đã commit thì không ghi audit "ké" được — ném rõ ràng, không lặng lẽ ghi trên kết nối khác.</summary>
    [Fact]
    public async Task Transaction_da_dong_thi_nem()
    {
        await using var scope = _services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

        await using var tx = await identity.Database.BeginTransactionAsync();
        var dbTx = tx.GetDbTransaction();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendAsync(dbTx, LockEntry()));
        Assert.Equal("0", await ScalarAsync("select count(*)::text from moderation.audit_logs"));
    }

    /// <summary>
    /// <c>tx == null</c> là kết nối RIÊNG (lời hứa của <see cref="IAuditTrail"/>): ghi <c>access.denied</c> trong lúc CHÍNH
    /// <c>ModerationDbContext</c> của cùng scope đang giữ transaction, rồi transaction đó rollback → dòng audit VẪN còn, còn thứ ghi
    /// trong transaction thì mất (đối chứng: đúng là có transaction đang mở và đã rollback). Việc treo của D7c (2026-09-25): bản đầu
    /// dùng kết nối scoped nên dòng này biến mất cùng rollback — ca đỏ với bản đó.
    /// </summary>
    [Fact]
    public async Task Khong_tx_song_sot_khi_transaction_cua_scope_rollback()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

            await using var tx = await db.Database.BeginTransactionAsync();
            await audit.AppendAsync(tx.GetDbTransaction(), LockEntry());   // đối chứng: phải mất theo rollback
            await audit.AppendAsync(null, new AuditEntry(Admin, AuditActions.AccessDenied, "endpoint", null));
            await tx.RollbackAsync();
        }

        Assert.Equal("access.denied", await ScalarAsync("select string_agg(action, ',') from moderation.audit_logs"));
    }

    private static AuditEntry LockEntry() => new(
        Admin, AuditActions.UserLock, "user", Target, new Dictionary<string, object?> { ["revocation"] = "applied" });

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
