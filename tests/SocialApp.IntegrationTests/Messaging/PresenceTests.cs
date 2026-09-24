using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Realtime;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// C5 (giai-doan-5.md Đ-5.11) — <see cref="IPresenceReader"/> mà GĐ6 dùng để quyết định thông báo <c>message</c> (Đ-6.17).
/// Redis THẬT. Ca "instance chết" ghi thẳng một member hết hạn vào Redis — đúng dấu vết một instance bị <c>docker kill</c>
/// để lại (không có <c>OnDisconnectedAsync</c> nào chạy để xóa nó).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PresenceTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IPresenceReader Presence => factory.Service<IPresenceReader>();

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < until, $"Không tới trong 5 giây: {what}");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Ket_noi_thi_online_ngat_thi_offline()
    {
        var userId = Guid.NewGuid();
        Assert.False(await Presence.IsOnlineAsync(userId));

        var connection = await RealtimeTestClient.ConnectAsync(factory, new ModulesTestClient(factory).Http, userId);
        await EventuallyAsync(() => Presence.IsOnlineAsync(userId), "online sau khi kết nối");

        await connection.DisposeAsync();
        await EventuallyAsync(async () => !await Presence.IsOnlineAsync(userId), "offline sau khi ngắt");
    }

    /// <summary>Hai tab: đóng một tab vẫn online; đóng cả hai mới offline.</summary>
    [Fact]
    public async Task Hai_tab_dong_mot_tab_van_online()
    {
        var userId = Guid.NewGuid();
        var http = new ModulesTestClient(factory).Http;
        var tab1 = await RealtimeTestClient.ConnectAsync(factory, http, userId);
        await using var tab2 = await RealtimeTestClient.ConnectAsync(factory, http, userId);

        await tab1.DisposeAsync();
        await Task.Delay(300);

        Assert.True(await Presence.IsOnlineAsync(userId));
    }

    /// <summary>R5-06: instance chết để lại member không ai xóa — hết hạn thì KHÔNG còn online (không "online vĩnh viễn").</summary>
    [Fact]
    public async Task Member_het_han_cua_instance_da_chet_khong_con_online()
    {
        var userId = Guid.NewGuid();
        var expired = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        await redis.Database.SortedSetAddAsync(RedisPresenceTracker.Key(userId.ToString()), "ket-noi-cua-instance-da-chet", expired);

        Assert.False(await Presence.IsOnlineAsync(userId));
    }
}
