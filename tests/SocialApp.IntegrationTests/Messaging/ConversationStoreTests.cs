using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.Modules.Messaging.DependencyInjection;
using SocialApp.Modules.Messaging.Domain;
using SocialApp.Modules.Messaging.Infrastructure;
using SocialApp.SharedKernel.Ids;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// A5 + phần đồng thời của B3 (giai-doan-5.md Mục 10.1–10.2) ở tầng store, trên Postgres thật: get-or-create theo cặp,
/// transaction gửi tin Đ-5.4 (khóa dòng → kiểm trùng → cấp seq → INSERT), idempotency Đ-5.5, mốc Đ-5.6, chưa đọc Đ-5.14, và
/// EXPLAIN của hai truy vấn nóng. Tầng HTTP/hub của cùng các luật nằm ở test endpoint (khối D).
///
/// Đồng thời thật: mỗi lượt song song một scope (một DbContext, một kết nối) — chung một context là tuần tự hóa giả.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Guid A = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid B = Guid.Parse("01000000-0000-0000-0000-000000000000");
    private static readonly ConversationPair AB = ConversationPair.Of(A, B);

    private ServiceProvider _services = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        // Trần pool 10 cho DB của lớp này: MSG-C1 bắn 50 lượt song song, và container dùng chung max_connections = 100 với pool
        // rảnh của mọi DB khác — trần mặc định 80 đẩy cả bộ sang 53300 (đo 2026-09-24: MSG-C1, MSG-C3 đỏ khi chạy cả bộ, xanh khi
        // chạy riêng). 10 kết nối vẫn đủ để các lượt tranh khóa dòng thật; phần còn lại xếp hàng chờ kết nối.
        _cs = new NpgsqlConnectionStringBuilder(await postgres.CreateDatabaseAsync()) { MaxPoolSize = 10 }.ConnectionString;
        _services = new ServiceCollection().AddMessagingModule(_cs).BuildServiceProvider();
        await _services.MigrateMessagingModuleAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_cs);
        NpgsqlConnection.ClearPool(conn);
    }

    private async Task<T> WithStoreAsync<T>(Func<IConversationStore, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IConversationStore>());
    }

    private Task<Conversation> ConversationAsync(ConversationPair pair) =>
        WithStoreAsync(async s => (await s.GetOrCreateAsync(pair, Uuid7.New(), DateTimeOffset.UtcNow, default)).Conversation);

    private Task<SendOutcome> SendAsync(Conversation c, Guid sender, string content, Guid? clientMsgId = null) =>
        WithStoreAsync(s => s.SendAsync(
            new SendCommand(c.Id, sender == c.UserAId, sender, Uuid7.New(), content, clientMsgId ?? Guid.NewGuid(),
                DateTimeOffset.UtcNow),
            default));

    private Task<Conversation> ReloadAsync(Guid id) => WithStoreAsync(async s => (await s.FindAsync(id, default))!);

    [Fact]
    public async Task CONV_01_get_or_create_lan_hai_tra_cung_hoi_thoai_khong_tao_moi()
    {
        var first = await WithStoreAsync(s => s.GetOrCreateAsync(AB, Uuid7.New(), DateTimeOffset.UtcNow, default));
        var second = await WithStoreAsync(s => s.GetOrCreateAsync(AB, Uuid7.New(), DateTimeOffset.UtcNow, default));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Conversation.Id, second.Conversation.Id);
    }

    /// <summary>CONV-02: hai người cùng bấm "Nhắn tin" cho nhau — 20 lượt song song, đúng MỘT dòng, không lượt nào ném.</summary>
    [Fact]
    public async Task CONV_02_hai_muoi_luot_song_song_dung_mot_hoi_thoai()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            WithStoreAsync(s => s.GetOrCreateAsync(
                i % 2 == 0 ? ConversationPair.Of(A, B) : ConversationPair.Of(B, A), Uuid7.New(), DateTimeOffset.UtcNow, default))));

        Assert.Single(results.Select(r => r.Conversation.Id).Distinct());
        Assert.Single(results, r => r.Created);
    }

    /// <summary>MSG-01 ở tầng store: seq = 1, con trỏ tin cuối trỏ đúng tin, mốc đã xem của NGƯỜI GỬI = 1 (Đ-5.14).</summary>
    [Fact]
    public async Task MSG_01_tin_dau_seq_1_con_tro_va_moc_nguoi_gui()
    {
        var c = await ConversationAsync(AB);

        var sent = await SendAsync(c, A, "xin chào");
        var after = await ReloadAsync(c.Id);

        Assert.Equal(SendStatus.Created, sent.Status);
        Assert.Equal(1, sent.Message!.Seq);
        Assert.Equal(1, after.SeqCounter);
        Assert.Equal(sent.Message.Id, after.LastMessageId);
        Assert.NotNull(after.LastMessageAt);
        Assert.Equal(new ReceiptMarks(1, 1), after.MarksOf(A));
        Assert.Equal(new ReceiptMarks(0, 0), after.MarksOf(B));
    }

    /// <summary>MSG-06 / MSG-07 ở tầng store (Đ-5.5): cùng id + cùng nội dung → đúng tin cũ; khác nội dung → reused.</summary>
    [Fact]
    public async Task MSG_06_07_gui_lai_cung_client_msg_id()
    {
        var c = await ConversationAsync(AB);
        var clientMsgId = Guid.NewGuid();

        var first = await SendAsync(c, A, "một", clientMsgId);
        var replay = await SendAsync(c, A, "một", clientMsgId);
        var reused = await SendAsync(c, A, "khác", clientMsgId);

        Assert.Equal(SendStatus.Replayed, replay.Status);
        Assert.Equal(first.Message!.Id, replay.Message!.Id);
        Assert.Equal(first.Message.Seq, replay.Message.Seq);
        Assert.Equal(SendStatus.ClientMsgIdReused, reused.Status);
        Assert.Equal(1, (await ReloadAsync(c.Id)).SeqCounter);
    }

    /// <summary>MSG-C1: 50 lượt gửi song song, A và B xen nhau — seq đúng tập 1..50, không lỗ, không trùng.</summary>
    [Fact]
    public async Task MSG_C1_nam_muoi_luot_song_song_seq_1_den_50()
    {
        var c = await ConversationAsync(AB);

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => SendAsync(c, i % 2 == 0 ? A : B, $"tin {i}")));

        Assert.All(results, r => Assert.Equal(SendStatus.Created, r.Status));
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), results.Select(r => r.Message!.Seq).Order());
        Assert.Equal(50, (await ReloadAsync(c.Id)).SeqCounter);
    }

    /// <summary>MSG-C2: 10 lượt song song CÙNG clientMsgId — đúng 1 dòng, 1 Created + 9 Replayed cùng messageId, không ném.</summary>
    [Fact]
    public async Task MSG_C2_muoi_luot_cung_client_msg_id_dung_mot_tin()
    {
        var c = await ConversationAsync(AB);
        var clientMsgId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SendAsync(c, A, "cùng một tin", clientMsgId)));

        Assert.Single(results, r => r.Status == SendStatus.Created);
        Assert.Equal(9, results.Count(r => r.Status == SendStatus.Replayed));
        Assert.Single(results.Select(r => r.Message!.Id).Distinct());
        Assert.Equal(1, (await ReloadAsync(c.Id)).SeqCounter);
    }

    /// <summary>MSG-C3: gửi song song ở 5 hội thoại khác nhau — không deadlock, mỗi hội thoại seq 1..10 (khóa theo DÒNG).</summary>
    [Fact]
    public async Task MSG_C3_nam_hoi_thoai_song_song_khong_deadlock()
    {
        var conversations = new List<Conversation>();
        foreach (var _ in Enumerable.Range(0, 5))
            conversations.Add(await ConversationAsync(ConversationPair.Of(A, Uuid7.New())));

        await Task.WhenAll(conversations.SelectMany(c =>
            Enumerable.Range(0, 10).Select(i => SendAsync(c, i % 2 == 0 ? A : c.PeerOf(A), $"tin {i}"))));

        foreach (var c in conversations)
            Assert.Equal(10, (await ReloadAsync(c.Id)).SeqCounter);
    }

    /// <summary>MSG-03 / MSG-04 ở tầng store: 75 tin → trang 30/30/15 seq giảm dần; afterSeq=70 → 71..75 tăng dần.</summary>
    [Fact]
    public async Task MSG_03_04_lich_su_cuon_nguoc_va_lap_cho_ho()
    {
        var c = await ConversationAsync(AB);
        for (var i = 0; i < 75; i++)
            await SendAsync(c, i % 2 == 0 ? A : B, $"tin {i}");

        var p1 = await WithStoreAsync(s => s.HistoryAsync(c.Id, null, 30, default));
        var p2 = await WithStoreAsync(s => s.HistoryAsync(c.Id, p1[^1].Seq, 30, default));
        var p3 = await WithStoreAsync(s => s.HistoryAsync(c.Id, p2[^1].Seq, 30, default));
        var after = await WithStoreAsync(s => s.AfterAsync(c.Id, 70, 50, default));

        Assert.Equal(Enumerable.Range(46, 30).Reverse().Select(i => (long)i), p1.Select(m => m.Seq));
        Assert.Equal(Enumerable.Range(16, 30).Reverse().Select(i => (long)i), p2.Select(m => m.Seq));
        Assert.Equal(Enumerable.Range(1, 15).Reverse().Select(i => (long)i), p3.Select(m => m.Seq));
        Assert.Equal([71L, 72, 73, 74, 75], after.Select(m => m.Seq));
    }

    /// <summary>LIST-01 ở tầng store: hội thoại hai phía, sắp last_message_at DESC, hội thoại rỗng không có; keyset nối tiếp.</summary>
    [Fact]
    public async Task LIST_01_hai_phia_sap_theo_tin_moi_nhat_khong_co_hoi_thoai_rong()
    {
        // A là user_a trong cặp (A, B) và là user_b trong cặp (Z, A) với Z nhỏ hơn A theo thứ tự Postgres.
        var z = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var withB = await ConversationAsync(AB);
        var withZ = await ConversationAsync(ConversationPair.Of(A, z));
        var withY = await ConversationAsync(ConversationPair.Of(A, Uuid7.New()));
        _ = await ConversationAsync(ConversationPair.Of(A, Uuid7.New()));   // rỗng — không được hiện

        await SendAsync(withB, B, "1");
        await SendAsync(withZ, z, "2");
        await SendAsync(withY, A, "3");
        await SendAsync(withB, A, "4");                                      // withB lên đầu

        var all = await WithStoreAsync(s => s.ListAsync(A, null, 10, default));
        var page1 = await WithStoreAsync(s => s.ListAsync(A, null, 2, default));
        var cursor = new ConversationCursor(page1[^1].LastMessageAt!.Value, page1[^1].Id);
        var page2 = await WithStoreAsync(s => s.ListAsync(A, cursor, 2, default));

        Assert.Equal([withB.Id, withY.Id, withZ.Id], all.Select(c => c.Id));
        Assert.Equal([withB.Id, withY.Id], page1.Select(c => c.Id));
        Assert.Equal([withZ.Id], page2.Select(c => c.Id));
    }

    /// <summary>MSG-10 ở tầng store (Đ-5.14): B offline nhận 3 tin → chưa đọc của B = 3, của A = 0; B xem tới 3 → 0.</summary>
    [Fact]
    public async Task MSG_10_chua_doc_chi_dem_tin_cua_nguoi_kia()
    {
        var c = await ConversationAsync(AB);
        for (var i = 0; i < 3; i++)
            await SendAsync(c, A, $"tin {i}");

        Assert.Equal(3, await WithStoreAsync(s => s.UnreadTotalAsync(B, default)));
        Assert.Equal(0, await WithStoreAsync(s => s.UnreadTotalAsync(A, default)));

        var marks = await WithStoreAsync(s => s.AdvanceMarksAsync(c.Id, isUserA: false, ReceiptKind.Seen, 3, DateTimeOffset.UtcNow, default));

        Assert.Equal(new ReceiptMarks(3, 3), marks);
        Assert.Equal(0, await WithStoreAsync(s => s.UnreadTotalAsync(B, default)));
    }

    /// <summary>RCP-01/02/03: seen 5 rồi seen 3 → không đổi (null); upToSeq vượt → kẹp; delivered không kéo seen.</summary>
    [Fact]
    public async Task RCP_01_02_03_moc_chi_tang_kep_va_seen_keo_delivered()
    {
        var c = await ConversationAsync(AB);
        for (var i = 0; i < 6; i++)
            await SendAsync(c, A, $"tin {i}");

        Task<ReceiptMarks?> Advance(ReceiptKind kind, long n) =>
            WithStoreAsync(s => s.AdvanceMarksAsync(c.Id, isUserA: false, kind, n, DateTimeOffset.UtcNow, default));

        Assert.Equal(new ReceiptMarks(2, 0), await Advance(ReceiptKind.Delivered, 2));
        Assert.Equal(new ReceiptMarks(5, 5), await Advance(ReceiptKind.Seen, 5));
        Assert.Null(await Advance(ReceiptKind.Seen, 3));
        Assert.Null(await Advance(ReceiptKind.Delivered, 4));
        Assert.Equal(new ReceiptMarks(6, 6), await Advance(ReceiptKind.Seen, 999));
        Assert.Null(await Advance(ReceiptKind.Seen, 999));
    }

    /// <summary>RCP-C1: 20 biên nhận seen song song với upToSeq ngẫu nhiên — mốc cuối = max, không lượt nào nổ CHECK.</summary>
    [Fact]
    public async Task RCP_C1_hai_muoi_bien_nhan_song_song_moc_cuoi_la_max()
    {
        var c = await ConversationAsync(AB);
        for (var i = 0; i < 20; i++)
            await SendAsync(c, A, $"tin {i}");

        var values = Enumerable.Range(0, 20).Select(_ => (long)Random.Shared.Next(1, 21)).ToList();
        await Task.WhenAll(values.Select(n =>
            WithStoreAsync(s => s.AdvanceMarksAsync(c.Id, isUserA: false, ReceiptKind.Seen, n, DateTimeOffset.UtcNow, default))));

        Assert.Equal(new ReceiptMarks(values.Max(), values.Max()), (await ReloadAsync(c.Id)).MarksOf(B));
    }

    /// <summary>
    /// Checklist Mục 12 + A5 "Xong khi": lịch sử là <c>Index Scan Backward</c> trên <c>uq_messages_conv_seq</c>, không Sort;
    /// danh sách dùng HAI index một phần. Bảng nhỏ thì planner hợp lý chọn Seq Scan — tắt nó để hỏi "index có DÙNG ĐƯỢC không".
    /// </summary>
    [Fact]
    public async Task EXPLAIN_lich_su_doc_nguoc_index_danh_sach_dung_hai_index_mot_phan()
    {
        var c = await ConversationAsync(AB);
        await SendAsync(c, A, "một");

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("set enable_seqscan = off");

        var history = string.Join('\n', await db.Database.SqlQuery<string>(
            $"""
            explain select * from messaging.messages
             where conversation_id = {c.Id} and seq < {100L} order by seq desc limit 31
            """).ToListAsync());
        var list = string.Join('\n', await db.Database.SqlQuery<string>(
            $"""
            explain select * from (
                (select * from messaging.conversations where user_a_id = {A} and last_message_at is not null
                  order by last_message_at desc, id desc limit 21)
                union all
                (select * from messaging.conversations where user_b_id = {A} and last_message_at is not null
                  order by last_message_at desc, id desc limit 21)
            ) x order by last_message_at desc, id desc limit 21
            """).ToListAsync());

        Assert.Contains("Index Scan Backward using uq_messages_conv_seq", history);
        Assert.DoesNotContain("Sort", history);
        Assert.Contains("idx_conversations_a_recent", list);
        Assert.Contains("idx_conversations_b_recent", list);
    }
}
