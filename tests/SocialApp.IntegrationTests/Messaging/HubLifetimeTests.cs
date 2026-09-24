using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Realtime;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// HUB-03 và HUB-10 (giai-doan-5.md Mục 6.4) — hai luật về THỜI GIAN, tách lớp riêng vì cần host cấu hình khác:
/// đồng hồ dịch được (<see cref="ShiftableTime"/>) cho hạn 30 giây của vé, và tuổi thọ kết nối rút xuống 1 giây
/// (<see cref="RealtimeOptions.MaxConnectionLifetime"/>) thay vì 15 phút.
/// </summary>
[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class HubLifetimeTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        factory.UseTestServices(services =>
        {
            services.AddSingleton<ShiftableTime>();
            services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<ShiftableTime>());
            services.Configure<RealtimeOptions>(o => o.MaxConnectionLifetime = TimeSpan.FromSeconds(1));
        });
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Http => new ModulesTestClient(factory).Http;

    /// <summary>HUB-03: vé quá 30 giây → 401. Đồng hồ nhảy 31 giây SAU khi cấp vé; TTL của Redis chưa kịp dọn khóa.</summary>
    [Fact]
    public async Task HUB_03_ve_qua_30_giay_401()
    {
        var clock = factory.Service<ShiftableTime>();
        var ticket = await RealtimeTestClient.IssueTicketAsync(Http, Guid.NewGuid());
        clock.Shift(TimeSpan.FromSeconds(31));
        try
        {
            await using var connection = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket));
            await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
        }
        finally
        {
            clock.Shift(TimeSpan.Zero);
        }
    }

    /// <summary>
    /// HUB-10: kết nối quá tuổi thọ tối đa → server cắt (Đ-5.10); client nối lại bằng vé MỚI thì được. Mỗi lần bắt tay xin đúng
    /// một vé — đếm số lần xin vé qua bộ đếm của factory ticket.
    /// </summary>
    [Fact]
    public async Task HUB_10_qua_tuoi_tho_server_cat_noi_lai_bang_ve_moi()
    {
        var http = Http;
        var userId = Guid.NewGuid();
        var issued = 0;
        await using var connection = RealtimeTestClient.Build(factory, async () =>
        {
            Interlocked.Increment(ref issued);
            return await RealtimeTestClient.IssueTicketAsync(http, userId);
        });

        await connection.StartAsync();
        await RealtimeTestClient.WaitClosedAsync(connection, TimeSpan.FromSeconds(10));

        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);
        Assert.Equal(2, issued);
    }

    /// <summary>Đồng hồ = đồng hồ hệ thống + độ lệch đặt tay. Timer vẫn là timer thật (tuổi thọ 1 giây chạy thật).</summary>
    public sealed class ShiftableTime : TimeProvider
    {
        private TimeSpan _offset;

        public void Shift(TimeSpan offset) => _offset = offset;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;
    }
}
