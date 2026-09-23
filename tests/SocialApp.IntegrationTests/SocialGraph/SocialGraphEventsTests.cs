using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Events;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// EVT-06 (C0 GĐ6, Đ-6.4) — event thật đầu tiên của hệ thống đi từ API tới handler: sau <c>COMMIT</c>, đúng số lần, <b>đúng vai</b>
/// từng id. Đổi chỗ hai id trong <c>new FriendRequestAccepted(…)</c> vẫn compile (cùng <see cref="Guid"/>), <c>FRD-*</c> vẫn xanh vì
/// không ai đọc event — và ở D10 thông báo "đã chấp nhận lời mời" tới chính người vừa bấm. Lớp này là lưới duy nhất.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SocialGraphEventsTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        factory.UseTestServices(services =>
        {
            // Singleton của HOST, không field của lớp test: xUnit dựng lại lớp test cho mỗi ca nhưng host dựng một lần cho cả
            // lớp — mỗi ca đọc lại từ factory.Services và lọc theo cặp id của mình.
            services.AddSingleton<Recorded>();
            services.AddIntegrationEventHandler<FriendRequestSent, RecordingHandler>();
            services.AddIntegrationEventHandler<FriendRequestAccepted, RecordingHandler>();
        });
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EVT_06_Gui_chap_nhan_phat_dung_vai_gui_trung_khong_phat()
    {
        var client = new ModulesTestClient(factory);
        var requester = Guid.NewGuid();
        var addressee = Guid.NewGuid();
        await client.PutProfileOkAsync(addressee, new { displayName = "Người dùng EVT-06" });

        await client.SendFriendRequestOkAsync(requester, addressee);
        await factory.DrainEventsAsync();

        var sent = Assert.IsType<FriendRequestSent>(Assert.Single(Mine()));
        Assert.Equal(requester, sent.RequesterId);
        Assert.Equal(addressee, sent.AddresseeId);

        await client.AcceptOkAsync(addressee, requester);
        await factory.DrainEventsAsync();

        var events = Mine();
        Assert.Equal(2, events.Count);
        var accepted = Assert.IsType<FriendRequestAccepted>(events[1]);
        Assert.Equal(requester, accepted.RequesterId);   // người GỬI lời mời — người sẽ nhận thông báo
        Assert.Equal(addressee, accepted.AccepterId);

        // Gửi lại khi đã là bạn: 409, không COMMIT gì → không event.
        using var again = await client.SendFriendRequestAsync(requester, new { userId = addressee });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await factory.DrainEventsAsync();
        Assert.Equal(2, Mine().Count);

        List<IIntegrationEvent> Mine() => factory.Services.GetRequiredService<Recorded>().Events.Where(e => e switch
        {
            FriendRequestSent s => s.RequesterId == requester || s.AddresseeId == requester,
            FriendRequestAccepted a => a.RequesterId == requester || a.AccepterId == requester,
            _ => false,
        }).ToList();
    }

    private sealed class Recorded
    {
        public ConcurrentQueue<IIntegrationEvent> Events { get; } = new();
    }

    private sealed class RecordingHandler(Recorded recorded)
        : IIntegrationEventHandler<FriendRequestSent>, IIntegrationEventHandler<FriendRequestAccepted>
    {
        public Task HandleAsync(FriendRequestSent integrationEvent, CancellationToken ct)
        {
            recorded.Events.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }

        public Task HandleAsync(FriendRequestAccepted integrationEvent, CancellationToken ct)
        {
            recorded.Events.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
