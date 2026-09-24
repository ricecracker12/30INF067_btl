using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Messaging.DependencyInjection;
using SocialApp.Modules.Messaging.Domain;
using SocialApp.Modules.Messaging.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Bản Messaging của <see cref="SocialGraphDbContextSchemaTests"/> (A2–A3, GĐ5 khối A). Mỗi khẳng định ứng với một luật của
/// giai-doan-5.md Mục 4 mà nếu cấu hình sai thì không lỗi nào khác báo — DB là lưới cuối của Đ-5.2, Đ-5.4, Đ-5.5, Đ-5.6.
///
/// <see cref="DisposeAsync"/> trả kết nối của database về Postgres ngay khi ca xong (<c>ClearPool</c>) — khuôn
/// <see cref="ModerationDbContextSchemaTests"/>. Không trả thì 12 ca ở đây để lại đủ kết nối rảnh đẩy bộ test sang
/// <c>53300 too many clients already</c> (đo 2026-09-24 khi thi công A3: 52 ca Auth/AuthZ đỏ đúng lỗi đó).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MessagingDbContextSchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
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

    private static readonly Guid OppositeA = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid OppositeB = Guid.Parse("01000000-0000-0000-0000-000000000000");

    private async Task<(ServiceProvider Services, string ConnectionString)> MigratedAsync(PostgresFixture postgres)
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection().AddMessagingModule(_connectionString).BuildServiceProvider();
        await _services.MigrateMessagingModuleAsync();
        return (_services, _connectionString);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<int> ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    private const string InsertConversation =
        """
        insert into messaging.conversations (id, user_a_id, user_b_id)
        values ('0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20', '00000001-0000-0000-0000-000000000000', '01000000-0000-0000-0000-000000000000')
        """;

    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_messaging()
    {
        var (services, _) = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value" from information_schema.tables where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([MessagingDbContext.Schema], schemas);
    }

    /// <summary>Migrate lần hai trên cùng DB không làm gì và không ném (checklist Mục 12 "--migrate chạy hai lần").</summary>
    [Fact]
    public async Task Migrate_lan_hai_khong_doi_gi()
    {
        var (services, _) = await MigratedAsync(postgres);
        await services.MigrateMessagingModuleAsync();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    /// <summary>Đ-5.2: <c>user_a_id &gt; user_b_id</c> (theo thứ tự Postgres) bị <c>ck_conversations_order</c> chặn.</summary>
    [Fact]
    public async Task User_a_lon_hon_user_b_bi_chan()
    {
        var (_, cs) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(cs);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            """
            insert into messaging.conversations (id, user_a_id, user_b_id)
            values (gen_random_uuid(), '01000000-0000-0000-0000-000000000000', '00000001-0000-0000-0000-000000000000')
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_conversations_order", ex.ConstraintName);
    }

    /// <summary>Một hội thoại mỗi cặp: cặp thứ hai → <c>uq_conversations_pair</c>; ON CONFLICT DO NOTHING không ném.</summary>
    [Fact]
    public async Task Cap_trung_bi_chan_va_on_conflict_khong_nem()
    {
        var (_, cs) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(cs);
        await ExecAsync(conn, InsertConversation);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            """
            insert into messaging.conversations (id, user_a_id, user_b_id)
            values (gen_random_uuid(), '00000001-0000-0000-0000-000000000000', '01000000-0000-0000-0000-000000000000')
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("uq_conversations_pair", ex.ConstraintName);

        var inserted = await ExecAsync(conn,
            """
            insert into messaging.conversations (id, user_a_id, user_b_id)
            values (gen_random_uuid(), '00000001-0000-0000-0000-000000000000', '01000000-0000-0000-0000-000000000000')
            on conflict (user_a_id, user_b_id) do nothing
            """);
        Assert.Equal(0, inserted);
    }

    /// <summary>Đ-5.6: mốc vượt <c>seq_counter</c>, hoặc đã xem vượt đã nhận → <c>ck_conversations_marks</c>.</summary>
    [Theory]
    [InlineData("user_b_delivered_seq = 1")]
    [InlineData("seq_counter = 5, user_a_seen_seq = 3, user_a_delivered_seq = 2")]
    public async Task Moc_sai_bat_bien_bi_chan(string set)
    {
        var (_, cs) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(cs);
        await ExecAsync(conn, InsertConversation);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            $"update messaging.conversations set {set}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_conversations_marks", ex.ConstraintName);
    }

    /// <summary>Hai lưới của Đ-5.4/Đ-5.5: trùng <c>seq</c> hoặc trùng <c>client_msg_id</c> trong một hội thoại → 23505.</summary>
    [Theory]
    [InlineData(1, "6f1d2c3b-4a5e-4f60-9b7a-8c9d0e1f2a3c", "uq_messages_conv_seq")]
    [InlineData(2, "6f1d2c3b-4a5e-4f60-9b7a-8c9d0e1f2a3b", "uq_messages_conv_client_id")]
    public async Task Trung_seq_hoac_client_msg_id_bi_chan(int seq, string clientMsgId, string constraint)
    {
        var (_, cs) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(cs);
        await ExecAsync(conn, InsertConversation);
        await ExecAsync(conn,
            """
            insert into messaging.messages (id, conversation_id, sender_id, seq, content, client_msg_id)
            values (gen_random_uuid(), '0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20', '00000001-0000-0000-0000-000000000000', 1, 'một',
                    '6f1d2c3b-4a5e-4f60-9b7a-8c9d0e1f2a3b')
            """);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            $"""
            insert into messaging.messages (id, conversation_id, sender_id, seq, content, client_msg_id)
            values (gen_random_uuid(), '0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20', '00000001-0000-0000-0000-000000000000', {seq}, 'hai',
                    '{clientMsgId}')
            """));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    /// <summary>Nội dung toàn dấu cách, <c>seq = 0</c> và hội thoại không tồn tại đều bị DB chặn (lưới cuối của service).</summary>
    [Theory]
    [InlineData("1", "'   '", "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20", "ck_messages_content")]
    [InlineData("0", "'ok'", "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20", "ck_messages_seq_positive")]
    [InlineData("1", "'ok'", "0192f3c1-0000-7000-8000-000000000000", "fk_messages_conversation")]
    public async Task Tin_sai_luat_bi_chan(string seq, string content, string conversationId, string constraint)
    {
        var (_, cs) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(cs);
        await ExecAsync(conn, InsertConversation);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            $"""
            insert into messaging.messages (id, conversation_id, sender_id, seq, content, client_msg_id)
            values (gen_random_uuid(), '{conversationId}', '00000001-0000-0000-0000-000000000000', {seq}, {content},
                    gen_random_uuid())
            """));

        Assert.Equal(constraint, ex.ConstraintName);
    }

    /// <summary>
    /// PAIR-01 (Mục 10.1, Đ-5.2): 200 cặp uuid ngẫu nhiên — cả UUID v7 lẫn v4 — chuẩn hóa qua <see cref="ConversationPair.Of"/>
    /// rồi INSERT qua EF: không cặp nào đỏ <c>ck_conversations_order</c>. Kèm cặp đối nghịch hex/byte.
    /// </summary>
    [Fact]
    public async Task PAIR_01_hai_tram_cap_ngau_nhien_khong_cap_nao_do_ck_order()
    {
        var (services, _) = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

        var pairs = Enumerable.Range(0, 200)
            .Select(i => i % 2 == 0
                ? ConversationPair.Of(SharedKernel.Ids.Uuid7.New(), SharedKernel.Ids.Uuid7.New())
                : ConversationPair.Of(Guid.NewGuid(), Guid.NewGuid()))
            .Append(ConversationPair.Of(OppositeB, OppositeA))
            .ToList();

        foreach (var pair in pairs)
            db.Conversations.Add(Conversation.Start(SharedKernel.Ids.Uuid7.New(), pair, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
        Assert.Equal(pairs.Count, await db.Conversations.CountAsync());
    }
}
