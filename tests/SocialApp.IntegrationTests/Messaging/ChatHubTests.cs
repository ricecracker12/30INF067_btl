using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.Modules.Messaging.Domain;
using SocialApp.SharedKernel.Authentication;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// Hai phương thức hub (D8) qua client SignalR thật (<see cref="RealtimeTestClient"/>): HUB-04b, HUB-07..09 là quyền ở cửa hub
/// — category <c>AuthZ</c> để cùng cổng CI với matrix (Mục 6.4); HUB-20/21 là chức năng.
///
/// Lỗi hub tới client dưới dạng <see cref="HubException"/> mà <c>Message</c> KẾT THÚC bằng mã (SignalR bọc thêm câu dẫn phía
/// trước khi <c>EnableDetailedErrors = false</c>) — FE đọc mã sau dấu "HubException: " (chat-hub-v1.md Mục 2).
/// </summary>
[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class ChatHubTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MessagingTestClient Rest => new(factory);

    private Task<HubConnection> ConnectAsync(Guid userId) =>
        RealtimeTestClient.ConnectAsync(factory, Rest.Modules.Http, userId);

    private async Task<(MessagingTestClient Http, Guid A, Guid B, Guid ConversationId)> FriendsAsync()
    {
        var rest = Rest;
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await rest.FriendsAsync(a, b);
        return (rest, a, b, (await rest.OpenOkAsync(a, b)).ConversationId);
    }

    private static Task<SendMessageResult> SendAsync(HubConnection connection, Guid conversationId, string content, Guid? id = null) =>
        connection.InvokeAsync<SendMessageResult>(
            "SendMessage", new SendMessageArgs(conversationId, content, id ?? Guid.NewGuid()));

    private static async Task AssertHubErrorAsync(string code, Func<Task> call)
    {
        var ex = await Assert.ThrowsAsync<HubException>(call);
        Assert.EndsWith($": {code}", ex.Message);
    }

    /// <summary>Ghi lại mọi <c>MessageReceived</c> một kết nối nhận được.</summary>
    private static ConcurrentQueue<MessageResponse> Received(HubConnection connection)
    {
        var box = new ConcurrentQueue<MessageResponse>();
        connection.On<MessageResponse>("MessageReceived", box.Enqueue);
        return box;
    }

    private static async Task EventuallyAsync(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, $"Không tới trong {Wait.TotalSeconds} giây: {what}");
            await Task.Delay(20);
        }
    }

    /// <summary>HUB-07: C (vé hợp lệ) gửi vào hội thoại A–B → <c>forbidden</c>, KHÔNG có dòng mới trong <c>messages</c>.</summary>
    [Fact]
    public async Task HUB_07_nguoi_thu_ba_gui_vao_hoi_thoai_nguoi_khac_forbidden()
    {
        var (rest, a, _, conversationId) = await FriendsAsync();
        await using var c = await ConnectAsync(Guid.NewGuid());

        await AssertHubErrorAsync("forbidden", () => SendAsync(c, conversationId, "chen ngang"));

        Assert.Empty((await rest.HistoryOkAsync(a, conversationId)).Items);
    }

    /// <summary>HUB-08: A gửi qua hub sau khi hủy kết bạn → <c>not-friends</c> (BR-09 ở cửa hub).</summary>
    [Fact]
    public async Task HUB_08_huy_ket_ban_roi_gui_qua_hub_not_friends()
    {
        var (rest, a, b, conversationId) = await FriendsAsync();
        await rest.Modules.UnfriendOkAsync(a, b);
        await using var connection = await ConnectAsync(a);

        await AssertHubErrorAsync("not-friends", () => SendAsync(connection, conversationId, "sau khi hủy"));
    }

    /// <summary>
    /// HUB-09 — IDOR chiều NGHE: A gửi → kết nối của A và B nhận <c>MessageReceived</c>; kết nối của C không nhận gì trong 2 giây.
    /// Đỏ khi <c>IUserIdProvider</c> sai hoặc ai đó đổi sang <c>Clients.All</c> (Đ-5.8).
    /// </summary>
    [Fact]
    public async Task HUB_09_chi_hai_thanh_vien_nhan_tin()
    {
        var (_, a, b, conversationId) = await FriendsAsync();
        await using var ca = await ConnectAsync(a);
        await using var cb = await ConnectAsync(b);
        await using var cc = await ConnectAsync(Guid.NewGuid());
        var (ra, rb, rc) = (Received(ca), Received(cb), Received(cc));

        var ack = await SendAsync(ca, conversationId, "chỉ hai người");

        await EventuallyAsync(() => rb.Any(m => m.MessageId == ack.Message.MessageId), "B nhận tin");
        await EventuallyAsync(() => ra.Any(m => m.MessageId == ack.Message.MessageId), "A nhận bản sao của chính mình");
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Empty(rc);
    }

    /// <summary>
    /// HUB-04b: đang kết nối, bị <c>revoked:user</c>, rồi gọi <c>SendMessage</c> → server cắt kết nối (Đ-5.10) và tin không lưu.
    /// </summary>
    [Fact]
    public async Task HUB_04b_bi_thu_hoi_giua_chung_goi_phuong_thuc_thi_bi_cat()
    {
        var (rest, a, _, conversationId) = await FriendsAsync();
        await using var connection = RealtimeTestClient.Build(factory, async () =>
            await RealtimeTestClient.IssueTicketAsync(rest.Modules.Http, a, issuedAt: DateTimeOffset.UtcNow.AddSeconds(-60)));
        await connection.StartAsync();
        await factory.Service<ITokenRevocationStore>().RevokeUserAsync(a, DateTimeOffset.UtcNow);

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(connection, conversationId, "sau khi bị thu hồi"));
        await RealtimeTestClient.WaitClosedAsync(connection, Wait);

        Assert.Empty((await rest.HistoryOkAsync(a, conversationId)).Items);
    }

    /// <summary>
    /// HUB-20: A gửi qua hub; B có HAI kết nối (hai tab) và A có kết nối thứ hai → ACK mang seq, cả ba kết nối nhận tin.
    /// Gửi lại cùng clientMsgId → ACK replayed, KHÔNG phát lần hai (Đ-5.5).
    /// </summary>
    [Fact]
    public async Task HUB_20_moi_tab_cua_hai_nguoi_nhan_tin_gui_lai_khong_phat_lan_hai()
    {
        var (_, a, b, conversationId) = await FriendsAsync();
        await using var a1 = await ConnectAsync(a);
        await using var a2 = await ConnectAsync(a);
        await using var b1 = await ConnectAsync(b);
        await using var b2 = await ConnectAsync(b);
        var (ra2, rb1, rb2) = (Received(a2), Received(b1), Received(b2));
        var clientMsgId = Guid.NewGuid();

        var ack = await SendAsync(a1, conversationId, "tin 1", clientMsgId);
        await EventuallyAsync(() => rb1.Count == 1 && rb2.Count == 1 && ra2.Count == 1, "ba kết nối nhận tin");
        var replay = await SendAsync(a1, conversationId, "tin 1", clientMsgId);
        await Task.Delay(500);

        Assert.False(ack.Replayed);
        Assert.Equal(1, ack.Message.Seq);
        Assert.True(replay.Replayed);
        Assert.Equal(ack.Message.MessageId, replay.Message.MessageId);
        Assert.Single(rb1);
        Assert.Single(rb2);
    }

    /// <summary>
    /// HUB-21: B gửi <c>SendReceipt(seen)</c> → A nhận <c>ReceiptUpdated</c> với mốc mới; gửi lại cùng mốc → không phát lần hai.
    /// </summary>
    [Fact]
    public async Task HUB_21_bien_nhan_seen_toi_nguoi_gui_gui_lai_khong_phat_lan_hai()
    {
        var (_, a, b, conversationId) = await FriendsAsync();
        await using var ca = await ConnectAsync(a);
        await using var cb = await ConnectAsync(b);
        var receipts = new ConcurrentQueue<ReceiptUpdatedEvent>();
        ca.On<ReceiptUpdatedEvent>("ReceiptUpdated", receipts.Enqueue);

        await SendAsync(ca, conversationId, "một");
        await SendAsync(ca, conversationId, "hai");
        await cb.InvokeAsync("SendReceipt", new SendReceiptArgs(conversationId, ReceiptKind.Seen, 2));
        await EventuallyAsync(() => receipts.Count == 1, "A nhận ReceiptUpdated");
        await cb.InvokeAsync("SendReceipt", new SendReceiptArgs(conversationId, ReceiptKind.Seen, 2));
        await Task.Delay(500);

        var only = Assert.Single(receipts);
        Assert.Equal(new ReceiptUpdatedEvent(conversationId, b, 2, 2), only);
    }

    /// <summary>Validation ở cửa hub (cùng luật REST): nội dung rỗng → <c>validation</c>; upToSeq 0 → <c>validation</c>.</summary>
    [Fact]
    public async Task Noi_dung_rong_hoac_moc_0_la_validation()
    {
        var (_, a, _, conversationId) = await FriendsAsync();
        await using var connection = await ConnectAsync(a);

        await AssertHubErrorAsync("validation", () => SendAsync(connection, conversationId, "   "));
        await AssertHubErrorAsync("validation",
            () => connection.InvokeAsync("SendReceipt", new SendReceiptArgs(conversationId, ReceiptKind.Seen, 0)));
    }
}
