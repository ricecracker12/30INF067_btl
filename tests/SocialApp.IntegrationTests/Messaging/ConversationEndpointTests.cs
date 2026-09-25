using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Messaging.Application;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.Modules.Messaging.Domain;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// Nghiệm thu REST của khối D (giai-doan-5.md Mục 10.1) trên Postgres thật, qua đúng pipeline HTTP. Bạn bè dựng qua API
/// SocialGraph thật (GĐ4). Luật đồng thời ở tầng store nằm ở <see cref="ConversationStoreTests"/>; quyền ở AuthZ matrix + hub.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationEndpointTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly CapturingLogSink Logs = new();

    public Task InitializeAsync()
    {
        factory.UseTestServices(s => s.AddSingleton<ILogEventSink>(Logs));
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MessagingTestClient Client => new(factory);

    private async Task<(MessagingTestClient Client, Guid A, Guid B, ConversationResponse Conversation)> FriendsWithConversationAsync()
    {
        var client = Client;
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await client.FriendsAsync(a, b);
        return (client, a, b, await client.OpenOkAsync(a, b));
    }

    private static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;

    /// <summary>CONV-01: 201 lần đầu, 200 lần sau với CÙNG hội thoại — và người kia mở cũng ra hội thoại đó.</summary>
    [Fact]
    public async Task CONV_01_mo_hoi_thoai_201_roi_200_cung_id()
    {
        var client = Client;
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await client.FriendsAsync(a, b);

        using var first = await client.OpenAsync(a, new { userId = b });
        using var again = await client.OpenAsync(a, new { userId = b });
        using var fromB = await client.OpenAsync(b, new { userId = a });
        var c1 = await first.Content.ReadFromJsonAsync<ConversationResponse>(ModulesTestClient.Json);
        var c2 = await again.Content.ReadFromJsonAsync<ConversationResponse>(ModulesTestClient.Json);
        var c3 = await fromB.Content.ReadFromJsonAsync<ConversationResponse>(ModulesTestClient.Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromB.StatusCode);
        Assert.Equal(c1!.ConversationId, c2!.ConversationId);
        Assert.Equal(c1.ConversationId, c3!.ConversationId);
        Assert.Equal(b, c1.Peer.UserId);
        Assert.Equal(a, c3.Peer.UserId);
        Assert.True(c1.CanSend);
        Assert.Null(c1.LastMessage);
    }

    /// <summary>CONV-03, CONV-04, BR-09: chính mình 400 · không có hồ sơ 404 · có hồ sơ nhưng không phải bạn 403 not-friends.</summary>
    [Fact]
    public async Task CONV_03_04_chinh_minh_400_khong_ho_so_404_nguoi_la_403()
    {
        var client = Client;
        var (a, stranger) = (Guid.NewGuid(), Guid.NewGuid());
        await client.Modules.PutProfileOkAsync(a, new { displayName = "Người A" });
        await client.Modules.PutProfileOkAsync(stranger, new { displayName = "Người lạ" });

        using var self = await client.OpenAsync(a, new { userId = a });
        using var ghost = await client.OpenAsync(a, new { userId = Guid.NewGuid() });
        using var notFriend = await client.OpenAsync(a, new { userId = stranger });

        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(["Không thể nhắn tin cho chính mình."], ((ValidationProblemDetails)await ProblemAsync(self)).Errors["userId"]);
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notFriend.StatusCode);
        Assert.Equal(MessagingErrors.NotFriendsType, (await ProblemAsync(notFriend)).Type);
    }

    /// <summary>MSG-01 qua HTTP: 201, seq 1; chi tiết thấy tin cuối; chưa đọc của B = 1, của A = 0 (Đ-5.14).</summary>
    [Fact]
    public async Task MSG_01_gui_tin_201_seq_1_tin_cuoi_va_chua_doc()
    {
        var (client, a, b, c) = await FriendsWithConversationAsync();

        var sent = await client.SendOkAsync(a, c.ConversationId, "xin chào");
        var fromB = await client.GetOkAsync(b, c.ConversationId);
        var fromA = await client.GetOkAsync(a, c.ConversationId);

        Assert.Equal(1, sent.Seq);
        Assert.Equal(a, sent.SenderId);
        Assert.Equal(sent.MessageId, fromB.LastMessage!.MessageId);
        Assert.Equal(1, fromB.UnreadCount);
        Assert.Equal(0, fromA.UnreadCount);
        Assert.Equal(1, await client.UnreadOkAsync(b));
    }

    /// <summary>MSG-02: rỗng, toàn khoảng trắng, 2001 ký tự → 400 errors.content; thiếu clientMsgId → 400.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    [InlineData(null)]
    public async Task MSG_02_noi_dung_sai_400_errors_content(string? content)
    {
        var (client, a, _, c) = await FriendsWithConversationAsync();

        using var response = await client.SendMessageAsync(a, c.ConversationId, new { content, clientMsgId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(((ValidationProblemDetails)await ProblemAsync(response)).Errors.ContainsKey("content"));
    }

    [Fact]
    public async Task MSG_02b_2001_ky_tu_400_2000_ky_tu_201()
    {
        var (client, a, _, c) = await FriendsWithConversationAsync();

        using var tooLong = await client.SendMessageAsync(
            a, c.ConversationId, new { content = new string('x', 2001), clientMsgId = Guid.NewGuid() });
        await client.SendOkAsync(a, c.ConversationId, new string('x', 2000));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal([MessageContentPolicy.TooLong], ((ValidationProblemDetails)await ProblemAsync(tooLong)).Errors["content"]);
    }

    /// <summary>MSG-03 / MSG-04 / MSG-05 qua HTTP: 75 tin → 30/30/15 seq giảm, nextCursor null ở cuối; afterSeq; cursor rác 400.</summary>
    [Fact]
    public async Task MSG_03_04_05_lich_su_phan_trang_lap_cho_ho_va_cursor_rac()
    {
        var (client, a, b, c) = await FriendsWithConversationAsync();
        for (var i = 0; i < 75; i++)
            await client.SendOkAsync(i % 2 == 0 ? a : b, c.ConversationId, $"tin {i}");

        var p1 = await client.HistoryOkAsync(b, c.ConversationId);
        var p2 = await client.HistoryOkAsync(b, c.ConversationId, $"?cursor={p1.NextCursor}");
        var p3 = await client.HistoryOkAsync(b, c.ConversationId, $"?cursor={p2.NextCursor}");
        var after = await client.HistoryOkAsync(b, c.ConversationId, "?afterSeq=70");
        using var garbage = await client.HistoryAsync(b, c.ConversationId, "?cursor=%%%rac");
        using var both = await client.HistoryAsync(b, c.ConversationId, $"?cursor={p1.NextCursor}&afterSeq=3");
        using var badLimit = await client.HistoryAsync(b, c.ConversationId, "?limit=51");

        Assert.Equal((30, 30, 15), (p1.Items.Count, p2.Items.Count, p3.Items.Count));
        Assert.Equal(75, p1.Items[0].Seq);
        Assert.Equal(1, p3.Items[^1].Seq);
        Assert.Null(p3.NextCursor);
        Assert.Equal([71L, 72, 73, 74, 75], after.Items.Select(m => m.Seq));
        Assert.Null(after.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
    }

    /// <summary>MSG-06 / MSG-07 qua HTTP (Đ-5.5): gửi lại cùng id + cùng nội dung → 200 ĐÚNG tin cũ; khác nội dung → 409.</summary>
    [Fact]
    public async Task MSG_06_07_gui_lai_200_tin_cu_khac_noi_dung_409()
    {
        var (client, a, _, c) = await FriendsWithConversationAsync();
        var clientMsgId = Guid.NewGuid();
        var first = await client.SendOkAsync(a, c.ConversationId, "một", clientMsgId);

        using var replay = await client.SendMessageAsync(a, c.ConversationId, new { content = "một", clientMsgId });
        using var reused = await client.SendMessageAsync(a, c.ConversationId, new { content = "hai", clientMsgId });
        var replayed = await replay.Content.ReadFromJsonAsync<MessageResponse>(ModulesTestClient.Json);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first.MessageId, replayed!.MessageId);
        Assert.Equal(first.Seq, replayed.Seq);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Single((await client.HistoryOkAsync(a, c.ConversationId)).Items);
    }

    /// <summary>MSG-10 + RCP qua HTTP: B xem tới 3 → chưa đọc 0; A thấy peerSeenSeq = 3; upToSeq vượt → 204, kẹp.</summary>
    [Fact]
    public async Task MSG_10_RCP_bien_nhan_ha_chua_doc_va_nguoi_gui_thay_moc()
    {
        var (client, a, b, c) = await FriendsWithConversationAsync();
        for (var i = 0; i < 3; i++)
            await client.SendOkAsync(a, c.ConversationId, $"tin {i}");
        Assert.Equal(3, await client.UnreadOkAsync(b));

        using var delivered = await client.ReceiptAsync(b, c.ConversationId, new { kind = "delivered", upToSeq = 99 });
        var afterDelivered = await client.GetOkAsync(a, c.ConversationId);
        using var seen = await client.ReceiptAsync(b, c.ConversationId, new { kind = "seen", upToSeq = 3 });
        var afterSeen = await client.GetOkAsync(a, c.ConversationId);
        using var badKind = await client.ReceiptAsync(b, c.ConversationId, new { kind = "read", upToSeq = 3 });
        using var zero = await client.ReceiptAsync(b, c.ConversationId, new { kind = "seen", upToSeq = 0 });

        Assert.Equal(HttpStatusCode.NoContent, delivered.StatusCode);
        Assert.Equal((3L, 0L), (afterDelivered.PeerDeliveredSeq, afterDelivered.PeerSeenSeq));
        Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
        Assert.Equal((3L, 3L), (afterSeen.PeerDeliveredSeq, afterSeen.PeerSeenSeq));
        Assert.Equal(0, await client.UnreadOkAsync(b));
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    /// <summary>LIST-01 qua HTTP: 25 hội thoại có tin + 3 rỗng → trang 20 + 5, mới nhất trước, không có hội thoại rỗng.</summary>
    [Fact]
    public async Task LIST_01_hai_muoi_lam_hoi_thoai_trang_20_va_5_khong_co_hoi_thoai_rong()
    {
        var client = Client;
        var a = Guid.NewGuid();
        var withMessages = new List<Guid>();
        for (var i = 0; i < 28; i++)
        {
            var peer = Guid.NewGuid();
            await client.FriendsAsync(a, peer);
            var c = await client.OpenOkAsync(a, peer);
            if (i < 25)
            {
                await client.SendOkAsync(i % 2 == 0 ? a : peer, c.ConversationId, $"tin {i}");
                withMessages.Add(c.ConversationId);
            }
        }

        var page1 = await client.ListOkAsync(a);
        var page2 = await client.ListOkAsync(a, $"?cursor={page1.NextCursor}");

        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.Null(page2.NextCursor);
        Assert.Equal(Enumerable.Reverse(withMessages), page1.Items.Concat(page2.Items).Select(x => x.ConversationId));
        Assert.All(page1.Items, x => Assert.Null(x.CanSend));
        Assert.All(page1.Items, x => Assert.NotNull(x.LastMessage));
    }

    /// <summary>LIST-02: số câu SQL của một trang KHÔNG đổi khi trang có 1 hay 20 hội thoại (không N+1 — nếp FEED-Q1).</summary>
    [Fact]
    public async Task LIST_02_so_cau_SQL_khong_doi_theo_so_hoi_thoai()
    {
        var client = Client;
        var (few, many) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedConversationsAsync(client, few, 1);
        await SeedConversationsAsync(client, many, 20);

        // Dựng cảnh phát event (lời mời, chấp nhận, tin nhắn) → handler thông báo của GĐ6 chạy NỀN trên cùng database. Chờ bus rỗng
        // trước khi đếm, không thì câu SQL của handler rơi vào khung đếm (sửa 2026-09-25, lộ ra khi C6 thêm bước đọc lại để đẩy hub).
        await factory.DrainEventsAsync();

        using var counter = new SqlCommandCounter(factory.ConnectionString);
        await client.ListOkAsync(few);   // làm nóng: cache quyền, pool, model EF

        counter.Reset();
        Assert.Single((await client.ListOkAsync(few)).Items);
        var n1 = counter.Statements.Count;
        counter.Reset();
        Assert.Equal(20, (await client.ListOkAsync(many)).Items.Count);
        var n20 = counter.Statements.Count;

        Assert.True(n1 == n20, $"1 hội thoại: {n1} lệnh, 20 hội thoại: {n20} lệnh:{Environment.NewLine}"
            + string.Join(Environment.NewLine, counter.Statements));
    }

    private static async Task SeedConversationsAsync(MessagingTestClient client, Guid owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var peer = Guid.NewGuid();
            await client.FriendsAsync(owner, peer);
            var c = await client.OpenOkAsync(owner, peer);
            await client.SendOkAsync(peer, c.ConversationId, $"tin {i}");
        }
    }

    /// <summary>
    /// FRIEND-01 / AC-04 / BR-09: hủy kết bạn → gửi 403 not-friends (cả hai phía), canSend = false, lịch sử vẫn đọc được,
    /// biên nhận vẫn gửi được.
    /// </summary>
    [Fact]
    public async Task FRIEND_01_huy_ket_ban_hoi_thoai_chi_doc()
    {
        var (client, a, b, c) = await FriendsWithConversationAsync();
        await client.SendOkAsync(a, c.ConversationId, "trước khi hủy");
        await client.Modules.UnfriendOkAsync(b, a);

        using var sendA = await client.SendMessageAsync(a, c.ConversationId, new { content = "sau", clientMsgId = Guid.NewGuid() });
        using var sendB = await client.SendMessageAsync(b, c.ConversationId, new { content = "sau", clientMsgId = Guid.NewGuid() });
        var detail = await client.GetOkAsync(b, c.ConversationId);
        var history = await client.HistoryOkAsync(b, c.ConversationId);
        using var receipt = await client.ReceiptAsync(b, c.ConversationId, new { kind = "seen", upToSeq = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, sendA.StatusCode);
        Assert.Equal(MessagingErrors.NotFriendsType, (await ProblemAsync(sendA)).Type);
        Assert.Equal(HttpStatusCode.Forbidden, sendB.StatusCode);
        Assert.False(detail.CanSend);
        Assert.Single(history.Items);
        Assert.Equal(HttpStatusCode.NoContent, receipt.StatusCode);
    }

    /// <summary>LOG-01 (Đ-5.18): nội dung tin không nằm trong log ở BẤT KỲ mức nào — kể cả khi gửi lại và khi bị từ chối.</summary>
    [Fact]
    public async Task LOG_01_noi_dung_tin_khong_nam_trong_log()
    {
        var (client, a, b, c) = await FriendsWithConversationAsync();
        var marker = $"SECRET-{Guid.NewGuid():N}";
        var clientMsgId = Guid.NewGuid();

        await client.SendOkAsync(a, c.ConversationId, marker, clientMsgId);
        using (await client.SendMessageAsync(a, c.ConversationId, new { content = marker, clientMsgId })) { }
        await client.Modules.UnfriendOkAsync(a, b);
        using (await client.SendMessageAsync(b, c.ConversationId, new { content = marker, clientMsgId = Guid.NewGuid() })) { }

        var leaked = Logs.Events.Where(e => e.RenderMessage().Contains(marker)
            || e.Properties.Values.Any(v => v.ToString().Contains(marker))
            || (e.Exception?.ToString().Contains(marker) ?? false)).ToList();
        Assert.NotEmpty(Logs.Events);   // chống xanh trong chân không: sink phải thật sự nhận log của app
        Assert.Empty(leaked);
    }

    /// <summary>JSON trên dây: enum chữ thường, không có trường status trên tin (Đ-5.6), canSend null trong danh sách.</summary>
    [Fact]
    public async Task Hinh_dang_JSON_khop_hop_dong()
    {
        var (client, a, _, c) = await FriendsWithConversationAsync();
        await client.SendOkAsync(a, c.ConversationId, "một");

        using var list = await client.ListAsync(a);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = doc.RootElement.GetProperty("items")[0];

        Assert.Equal(JsonValueKind.Null, item.GetProperty("canSend").ValueKind);
        Assert.False(item.GetProperty("lastMessage").TryGetProperty("status", out _));
        Assert.Equal(1, item.GetProperty("lastMessage").GetProperty("seq").GetInt64());
    }
}
