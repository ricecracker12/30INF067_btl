using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Presentation;
using SocialApp.SharedKernel.Authentication;
using Xunit;

namespace SocialApp.IntegrationTests.Notification;

/// <summary>
/// C6 (GĐ6, Đ-6.18) — hub <c>/hubs/notifications</c> trên vé realtime của GĐ5, Redis THẬT (vé nằm trong Redis). Category <c>AuthZ</c> như
/// <c>HubAuthZTests</c>: cửa thứ hai mà matrix HTTP không thấy, phải nằm trong cùng cổng CI.
///
/// Mọi kết nối đi đúng đường client thật: WebSockets + SkipNegotiation, vé trên query (<see cref="RealtimeTestClient"/>). Sự kiện sinh từ
/// API thật (lời mời kết bạn) → bus → handler → store → đẩy — không gọi pusher tay.
/// </summary>
[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class NotificationHubTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly TimeSpan ChoToi = TimeSpan.FromSeconds(5);

    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Http => new ModulesTestClient(factory).Http;

    private HubConnection Build(Func<Task<string?>> ticket) => RealtimeTestClient.Build(factory, ticket, NotificationHub.Path);

    private async Task<HubConnection> NoiAsync(Guid userId)
    {
        var connection = Build(async () => await RealtimeTestClient.IssueTicketAsync(Http, userId));
        await connection.StartAsync();
        return connection;
    }

    /// <summary>Ghi lại mọi <c>NotificationUpserted</c> một kết nối nhận, và cho chờ tới khi có đủ <c>n</c> sự kiện.</summary>
    private sealed class Hop
    {
        private readonly ConcurrentQueue<NotificationUpsertedEvent> _events = new();
        private readonly SemaphoreSlim _arrived = new(0);

        public Hop(HubConnection connection) =>
            connection.On<NotificationUpsertedEvent>(NotificationHubPusher.NotificationUpserted, e =>
            {
                _events.Enqueue(e);
                _arrived.Release();
            });

        public IReadOnlyList<NotificationUpsertedEvent> Events => [.. _events];

        public async Task ChoAsync(int n)
        {
            while (_events.Count < n)
                Assert.True(await _arrived.WaitAsync(ChoToi), $"Sau {ChoToi.TotalSeconds} giây mới nhận {_events.Count}/{n} sự kiện.");
        }
    }

    /// <summary>Đối chứng của cả nhóm: vé hợp lệ → bắt tay xong. Không có ca này thì mọi ca "401" xanh cả khi hub không được map.</summary>
    [Fact]
    public async Task NHUB_00_ve_hop_le_bat_tay_thanh_cong()
    {
        await using var connection = await NoiAsync(Guid.NewGuid());
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task NHUB_01_khong_ve_401()
    {
        await using var connection = Build(() => Task.FromResult<string?>(null));
        await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
    }

    /// <summary>Vé dùng một lần — dùng cho hub chat rồi thì không nối được hub thông báo (và ngược lại).</summary>
    [Fact]
    public async Task NHUB_02_ve_da_dung_401()
    {
        var ticket = await RealtimeTestClient.IssueTicketAsync(Http, Guid.NewGuid());
        await using (var chat = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket)))
            await chat.StartAsync();

        await using var connection = Build(() => Task.FromResult<string?>(ticket));
        await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
    }

    /// <summary>JWT truy cập đặt vào <c>?access_token=</c> bị từ chối — hub chỉ nhận vé (scheme <c>RealtimeTicket</c>).</summary>
    [Fact]
    public async Task NHUB_03_jwt_tren_query_401()
    {
        await using var connection = Build(() => Task.FromResult<string?>(TestJwt.Create("USER", Guid.NewGuid())));
        await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
    }

    /// <summary>
    /// <c>HUB-09</c> của thông báo: A và B cùng nối. C mời A → CHỈ A nhận <c>NotificationUpserted</c> (đúng loại, đúng actor, số chưa đọc
    /// tuyệt đối 1). Rồi D mời B → B nhận đúng MỘT sự kiện, của chính B: sự kiện của A mà lọt sang B thì đã tới trước (thứ tự trên một kết
    /// nối được giữ). Rồi E mời A → A nhận sự kiện thứ hai, số chưa đọc 2.
    /// </summary>
    [Fact]
    public async Task NHUB_09_chi_nguoi_nhan_duoc_day_dung_payload()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await client.PutProfileOkAsync(a, new { displayName = "Người nhận A" });
        await client.PutProfileOkAsync(b, new { displayName = "Người nhận B" });
        await using var noiA = await NoiAsync(a);
        await using var noiB = await NoiAsync(b);
        var hopA = new Hop(noiA);
        var hopB = new Hop(noiB);

        var c = Guid.NewGuid();
        await client.PutProfileOkAsync(c, new { displayName = "Người mời C" });
        await client.SendFriendRequestOkAsync(c, a);
        await factory.DrainEventsAsync();
        await hopA.ChoAsync(1);

        var suKien = Assert.Single(hopA.Events);
        Assert.Equal(("friend_request", c, "Người mời C", 1, "user", c, 1), (
            suKien.Notification.Type, suKien.Notification.Actor!.UserId, suKien.Notification.Actor.DisplayName,
            suKien.Notification.ActorCount, suKien.Notification.Target.Type, suKien.Notification.Target.Id, suKien.UnreadTotal));

        var d = Guid.NewGuid();
        await client.PutProfileOkAsync(d, new { displayName = "Người mời D" });
        await client.SendFriendRequestOkAsync(d, b);
        await factory.DrainEventsAsync();
        await hopB.ChoAsync(1);
        Assert.Equal(d, Assert.Single(hopB.Events).Notification.Actor!.UserId);

        var e = Guid.NewGuid();
        await client.PutProfileOkAsync(e, new { displayName = "Người mời E" });
        await client.SendFriendRequestOkAsync(e, a);
        await factory.DrainEventsAsync();
        await hopA.ChoAsync(2);
        Assert.Equal((e, 2), (hopA.Events[1].Notification.Actor!.UserId, hopA.Events[1].UnreadTotal));
        Assert.Single(hopB.Events);
    }
}
