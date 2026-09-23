using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Notification.DependencyInjection;
using SocialApp.Modules.Notification.Domain;
using SocialApp.Modules.Notification.Infrastructure;
using SocialApp.SharedKernel.Events;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Bản Notification của <see cref="ModerationDbContextSchemaTests"/> (A2, GĐ6 khối A). Mỗi khẳng định ứng với một ràng buộc mà
/// upsert gộp của D9 (Đ-6.16) dựa vào — cấu hình sai thì không có lỗi nào khác báo, chỉ có số trên chuông sai.
///
/// Mỗi test một database mới (<see cref="PostgresFixture.CreateDatabaseAsync"/>); <see cref="DisposeAsync"/> trả kết nối ngay
/// (<c>ClearPool</c>) — lý do ở <see cref="ModerationDbContextSchemaTests"/> (cạm bẫy 7 Mục 3 của hướng dẫn khối A+C).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NotificationDbContextSchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Guid Recipient = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Actor1 = Guid.Parse("01900000-0000-7000-8000-0000000000b1");
    private static readonly Guid Actor2 = Guid.Parse("01900000-0000-7000-8000-0000000000b2");
    private static readonly Guid Post = Guid.Parse("01900000-0000-7000-8000-0000000000c1");

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
            .AddNotificationModule(_connectionString)
            .BuildServiceProvider();

        await _services.MigrateNotificationModuleAsync();
        return (_services, _connectionString);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static Task<int> InsertAsync(NpgsqlConnection conn, Guid id, string groupKey, string type = "reaction",
        string targetType = "post", int actorCount = 1) => ExecuteAsync(conn, $"""
        insert into notification.notifications (id, recipient_id, type, group_key, target_type, target_id, last_actor_id, actor_count)
        values ('{id}', '{Recipient}', '{type}', '{groupKey}', '{targetType}', '{Post}', '{Actor1}', {actorCount})
        """);

    /// <summary>Bảng lịch sử của Notification nằm trong schema <c>notification</c>.</summary>
    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_notification()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value"
                from information_schema.tables
                where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([NotificationDbContext.Schema], schemas);
    }

    /// <summary>
    /// <c>uq_notifications_group</c> là UNIQUE CONSTRAINT (<c>contype = 'u'</c>) trên đúng <c>(recipient_id, group_key)</c> —
    /// hình dạng mà <c>ON CONFLICT (recipient_id, group_key)</c> của D9 dựa vào; chốt ở đây để D9 không phải đoán (cạm bẫy Mục 4
    /// của hướng dẫn khối A+C). Cặp trùng bị chặn bằng <c>23505</c>; cùng khóa khác người nhận thì được.
    /// </summary>
    [Fact]
    public async Task UNIQUE_recipient_group_key_la_constraint_va_chan_cap_trung()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        Assert.Equal(1, await CountAsync(conn, """
            select count(*) from pg_constraint c
            join pg_namespace n on n.oid = c.connamespace
            where n.nspname = 'notification' and c.conname = 'uq_notifications_group' and c.contype = 'u'
              and pg_get_constraintdef(c.oid) = 'UNIQUE (recipient_id, group_key)'
            """));

        var key = GroupKey.Reaction(ReactionTargetKind.Post, Post);
        await InsertAsync(conn, Guid.NewGuid(), key);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(conn, Guid.NewGuid(), key));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("uq_notifications_group", ex.ConstraintName);

        // Cùng group_key, người nhận khác → nhóm riêng của người đó.
        await ExecuteAsync(conn, $"""
            insert into notification.notifications (id, recipient_id, type, group_key, target_type, target_id)
            values (gen_random_uuid(), '{Actor2}', 'reaction', '{key}', 'post', '{Post}')
            """);
    }

    /// <summary>
    /// Câu gộp của D9 (Đ-6.16) chạy được trên constraint này: <c>ON CONFLICT (recipient_id, group_key) DO UPDATE</c> không tạo
    /// dòng thứ hai, và <c>xmax = 0</c> phân biệt "vừa tạo" với "vừa gộp" — D9 dựa vào cờ đó để không cộng <c>actor_count</c>
    /// cho dòng vừa tạo. Test khóa hình dạng câu, không khóa luật đợt (việc của D9).
    /// </summary>
    [Fact]
    public async Task On_conflict_do_update_gop_vao_mot_dong()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        var key = GroupKey.Comment(Post);
        var upsert = $"""
            insert into notification.notifications (id, recipient_id, type, group_key, target_type, target_id, last_actor_id)
            values (gen_random_uuid(), '{Recipient}', 'comment', '{key}', 'post', '{Post}', @actor)
            on conflict (recipient_id, group_key) do update
               set last_actor_id = excluded.last_actor_id, updated_at = now(), is_read = false
            returning (xmax = 0) as inserted
            """;

        async Task<bool> UpsertAsync(Guid actor)
        {
            await using var cmd = new NpgsqlCommand(upsert, conn);
            cmd.Parameters.AddWithValue("actor", actor);
            return (bool)(await cmd.ExecuteScalarAsync())!;
        }

        Assert.True(await UpsertAsync(Actor1));
        Assert.False(await UpsertAsync(Actor2));

        Assert.Equal(1, await CountAsync(conn, "select count(*) from notification.notifications"));
        Assert.Equal(1, await CountAsync(conn,
            $"select count(*) from notification.notifications where last_actor_id = '{Actor2}'"));
    }

    /// <summary>
    /// Hai CHECK chặn thật: <c>actor_count = 0</c> (<c>ck_notifications_actor_count</c>) và loại ngoài tám loại của Đ-6.17
    /// (<c>ck_notifications_type</c>). So cả tên constraint để biết CHECK nào bắt.
    /// </summary>
    [Theory]
    [InlineData("reaction", 0, "ck_notifications_actor_count")]
    [InlineData("like", 1, "ck_notifications_type")]
    public async Task CHECK_chan_that(string type, int actorCount, string constraint)
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(conn, Guid.NewGuid(), "k:1", type, actorCount: actorCount));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    /// <summary>
    /// Tám loại của <see cref="NotificationTypes.All"/> đều qua CHECK, và <c>target_type = 'conversation'</c> (12 ký tự) vừa cột —
    /// DDL bản đầu ghi <c>varchar(10)</c>, sẽ chặn mọi thông báo <c>message</c> bằng <c>22001</c>.
    /// </summary>
    [Fact]
    public async Task Tam_loai_va_dich_conversation_deu_chen_duoc()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        foreach (var type in NotificationTypes.All)
            await InsertAsync(conn, Guid.NewGuid(), $"{type}:x", type, NotificationTargetTypes.Conversation);

        Assert.Equal(NotificationTypes.All.Length, await CountAsync(conn, "select count(*) from notification.notifications"));
    }

    /// <summary>
    /// <c>notification_actors</c>: PK cặp chặn cùng người hai lần trong một nhóm (thứ làm <c>actor_count</c> đếm người khác nhau);
    /// xóa nhóm kéo theo người của nhóm (<c>ON DELETE CASCADE</c>, FK trong schema).
    /// </summary>
    [Fact]
    public async Task Actors_PK_cap_va_xoa_nhom_keo_theo_nguoi()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        var id = Guid.NewGuid();
        await InsertAsync(conn, id, GroupKey.Comment(Post));
        await ExecuteAsync(conn,
            $"insert into notification.notification_actors (notification_id, actor_id) values ('{id}', '{Actor1}'), ('{id}', '{Actor2}')");

        Assert.Equal(0, await ExecuteAsync(conn, $"""
            insert into notification.notification_actors (notification_id, actor_id) values ('{id}', '{Actor1}')
            on conflict do nothing
            """));

        await ExecuteAsync(conn, $"delete from notification.notifications where id = '{id}'");

        Assert.Equal(0, await CountAsync(conn, "select count(*) from notification.notification_actors"));
    }

    /// <summary>
    /// Hai index của Mục 4 có mặt đúng hình dạng: <c>idx_notifications_recent</c> sắp <c>updated_at DESC, id DESC</c> (khớp keyset
    /// của <c>NotificationPage</c>) và <c>idx_notifications_unread</c> là index một phần <c>WHERE is_read = false</c> (badge).
    /// </summary>
    [Fact]
    public async Task Hai_index_dung_hinh_dang()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        async Task<string> IndexDefAsync(string name)
        {
            await using var cmd = new NpgsqlCommand(
                "select indexdef from pg_indexes where schemaname = 'notification' and indexname = @name", conn);
            cmd.Parameters.AddWithValue("name", name);
            return (string)(await cmd.ExecuteScalarAsync() ?? "");
        }

        Assert.EndsWith("(recipient_id, updated_at DESC, id DESC)", await IndexDefAsync("idx_notifications_recent"));
        Assert.EndsWith("(recipient_id) WHERE (is_read = false)", await IndexDefAsync("idx_notifications_unread"));
    }
}
