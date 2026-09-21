using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.Modules.SocialGraph.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Bản SocialGraph của <see cref="ContentDbContextSchemaTests"/> (A3, GĐ4 khối A). Bảy khẳng định —
/// mỗi cái ứng với một thứ mà nếu cấu hình sai thì không có lỗi nào khác báo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SocialGraphDbContextSchemaTests(PostgresFixture postgres)
{
    // Cặp đối nghịch GUID-01: hex Postgres a < b; ToByteArray() đảo chiều ở byte đầu.
    private static readonly Guid OppositeA = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid OppositeB = Guid.Parse("01000000-0000-0000-0000-000000000000");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-21T12:00:00Z");

    private static async Task<(ServiceProvider Services, string ConnectionString)> MigratedAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection()
            .AddSocialGraphModule(connectionString)
            .BuildServiceProvider();

        await services.MigrateSocialGraphModuleAsync();
        return (services, connectionString);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>Khẳng định 1 — bảng lịch sử của SocialGraph nằm trong schema <c>socialgraph</c>.</summary>
    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_socialgraph()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value"
                from information_schema.tables
                where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([SocialGraphDbContext.Schema], schemas);
    }

    /// <summary>Khẳng định 2 — INSERT <c>user_min_id &gt; user_max_id</c> bị <c>ck_friendships_order</c> chặn.</summary>
    [Fact]
    public async Task User_min_lon_hon_user_max_bi_chan()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);
        await using var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at)
            values
                ('01000000-0000-0000-0000-000000000000',
                 '00000001-0000-0000-0000-000000000000',
                 '01000000-0000-0000-0000-000000000000',
                 'pending', now(), now())
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_friendships_order", ex.ConstraintName);
    }

    /// <summary>Khẳng định 3 — <c>requester_id</c> là người thứ ba bị <c>ck_friendships_requester</c> chặn.</summary>
    [Fact]
    public async Task Requester_ngoai_cap_bi_chan()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);
        await using var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at)
            values
                ('00000001-0000-0000-0000-000000000000',
                 '01000000-0000-0000-0000-000000000000',
                 '01900000-0000-7000-8000-00000000000c',
                 'pending', now(), now())
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_friendships_requester", ex.ConstraintName);
    }

    /// <summary>
    /// Khẳng định 4 — cả hai chiều của <c>ck_friendships_accepted</c>:
    /// accepted mà thiếu accepted_at, và pending mà có accepted_at.
    /// </summary>
    [Fact]
    public async Task Accepted_va_accepted_at_phai_di_cung_nhau()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);

        await using (var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
            values
                ('00000001-0000-0000-0000-000000000001',
                 '01000000-0000-0000-0000-000000000001',
                 '00000001-0000-0000-0000-000000000001',
                 'accepted', now(), now(), null)
            """, conn))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            Assert.Equal("ck_friendships_accepted", ex.ConstraintName);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
            values
                ('00000001-0000-0000-0000-000000000002',
                 '01000000-0000-0000-0000-000000000002',
                 '00000001-0000-0000-0000-000000000002',
                 'pending', now(), now(), now())
            """, conn))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            Assert.Equal("ck_friendships_accepted", ex.ConstraintName);
        }
    }

    /// <summary>
    /// Khẳng định 5 — INSERT lần hai cùng cặp (kể cả requester khác) bị PK chặn — nguồn 409 ở D2 / FRD-06.
    /// </summary>
    [Fact]
    public async Task Hai_dong_cung_cap_bi_chan_boi_PK()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);

        await using (var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at)
            values
                ('00000001-0000-0000-0000-000000000000',
                 '01000000-0000-0000-0000-000000000000',
                 '00000001-0000-0000-0000-000000000000',
                 'pending', now(), now())
            """, conn))
        {
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        await using (var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at)
            values
                ('00000001-0000-0000-0000-000000000000',
                 '01000000-0000-0000-0000-000000000000',
                 '01000000-0000-0000-0000-000000000000',
                 'pending', now(), now())
            """, conn))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
            Assert.Equal("PK_friendships", ex.ConstraintName);
        }
    }

    /// <summary>
    /// Khẳng định 6 — <see cref="FriendPair.Of"/> khớp thứ tự Postgres bằng Postgres thật:
    /// cặp đối nghịch lưu qua EF rồi đọc lại đúng Min/Max và status chữ thường.
    /// </summary>
    [Fact]
    public async Task Cap_doi_nghich_luu_duoc_qua_EF_dung_thu_tu_Postgres()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        db.Friendships.Add(Friendship.Request(OppositeA, OppositeB, Now));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var row = await db.Friendships.SingleAsync();

        Assert.Equal(OppositeA, row.UserMinId);
        Assert.Equal(OppositeB, row.UserMaxId);
        Assert.Equal(OppositeA, row.RequesterId);
        Assert.Equal(FriendshipStatus.Pending, row.Status);
        Assert.Null(row.AcceptedAt);
    }

    /// <summary>Khẳng định 7 — tự theo dõi bị <c>ck_follows_not_self</c> chặn.</summary>
    [Fact]
    public async Task Tu_theo_doi_bi_chan()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);
        await using var cmd = new NpgsqlCommand(
            """
            insert into socialgraph.follows (follower_id, followee_id, created_at)
            values ('00000001-0000-0000-0000-000000000000',
                    '00000001-0000-0000-0000-000000000000',
                    now())
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_follows_not_self", ex.ConstraintName);
    }
}
