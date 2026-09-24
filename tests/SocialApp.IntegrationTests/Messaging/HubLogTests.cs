using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using SocialApp.IntegrationTests.Harness;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// C6 (giai-doan-5.md Đ-5.18, ràng buộc Đ-E16): vé realtime nằm trên query string của <c>/hubs/*</c> nên KHÔNG được xuất hiện
/// trong log API ở bất kỳ mức nào. Host test chạy Development — mức log RỘNG nhất của dự án (<c>Debug</c>,
/// <c>Microsoft.AspNetCore = Information</c>) — nên xanh ở đây thì Staging/Production (hẹp hơn) cũng xanh.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HubLogTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly CapturingLogSink Logs = new();

    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        factory.UseTestServices(s => s.AddSingleton<ILogEventSink>(Logs));
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ve_tren_query_khong_nam_trong_log_khi_bat_tay_va_khi_bi_tu_choi()
    {
        var http = new ModulesTestClient(factory).Http;
        var ticket = await RealtimeTestClient.IssueTicketAsync(http, Guid.NewGuid());

        await using (var ok = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket)))
            await ok.StartAsync();
        await using (var reused = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket)))
            await RealtimeTestClient.AssertHandshakeRejectedAsync(reused);

        Assert.NotEmpty(Logs.Events);   // chống xanh trong chân không
        var leaked = Logs.Events
            .Where(e => e.RenderMessage().Contains(ticket)
                     || e.Properties.Values.Any(v => v.ToString().Contains(ticket))
                     || (e.Exception?.ToString().Contains(ticket) ?? false))
            .Select(e => e.MessageTemplate.Text)
            .ToList();
        Assert.True(leaked.Count == 0, "Vé lọt vào log qua: " + string.Join(" | ", leaked));
    }
}
