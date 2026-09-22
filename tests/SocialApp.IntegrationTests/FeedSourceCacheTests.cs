using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Application;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.Modules.SocialGraph.Infrastructure;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// C1 — cache nguồn feed trên Redis thật (Đ-4.8): trượt → DB → ghi; trúng → không SQL; InvalidateAsync
/// xóa cả hai khóa; công tắc tắt → luôn DB; Redis chết → DB + log cảnh báo; kết quả rỗng vẫn được cache.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedSourceCacheTests(PostgresFixture postgres, RedisFixture redis)
    : IClassFixture<RedisFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");

    [Fact]
    public async Task Truot_thi_doc_DB_ghi_khoa_TTL_60s()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var harness = await MigratedWithRedisAsync();
        await using var services = harness.Services;
        await SeedAcceptedAsync(services, a, b);

        var sources = await services.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Contains(b, sources.Friends);
        var raw = await redis.Database.StringGetAsync(CacheKey(a));
        Assert.True(raw.HasValue, "trượt cache phải ghi khóa sg:feed-sources:{id:D}");
        using var json = JsonDocument.Parse((string)raw!);
        Assert.Contains(
            json.RootElement.GetProperty("f").EnumerateArray().Select(e => e.GetGuid()),
            id => id == b);
        Assert.Empty(json.RootElement.GetProperty("fo").EnumerateArray());

        var ttl = await redis.Database.KeyTimeToLiveAsync(CacheKey(a));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 50, 60);
    }

    [Fact]
    public async Task Trung_thi_khong_lenh_SQL()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var harness = await MigratedWithRedisAsync();
        await using var services = harness.Services;
        await SeedAcceptedAsync(services, a, b);
        await services.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        using var counter = new SqlCommandCounter(harness.ConnectionString);
        counter.Reset();
        var sources = await services.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Contains(b, sources.Friends);
        Assert.True(
            counter.Statements.Count == 0,
            "trúng cache vẫn gửi SQL: " + string.Join(" | ", counter.Statements));
    }

    [Fact]
    public async Task InvalidateAsync_xoa_ca_hai_khoa()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var harness = await MigratedWithRedisAsync();
        await using var services = harness.Services;
        await SeedAcceptedAsync(services, a, b);

        var reader = services.GetRequiredService<IFeedSourceReader>();
        await reader.GetAsync(a);
        await reader.GetAsync(b);
        Assert.True(await redis.Database.KeyExistsAsync(CacheKey(a)));
        Assert.True(await redis.Database.KeyExistsAsync(CacheKey(b)));

        await services.GetRequiredService<IFeedSourceCache>().InvalidateAsync(a, b);

        Assert.False(await redis.Database.KeyExistsAsync(CacheKey(a)));
        Assert.False(await redis.Database.KeyExistsAsync(CacheKey(b)));
    }

    [Fact]
    public async Task Cong_tac_tat_thi_luon_DB_khong_ghi_khoa()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var cs = await postgres.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddSocialGraphModule(cs)
            .AddSharedKernelRedis(redis.ConnectionString)
            .AddLogging()
            .Configure<FeedSourceCacheOptions>(o => o.Enabled = false)
            .BuildServiceProvider();
        await services.MigrateSocialGraphModuleAsync();
        await WaitForRedisAsync(services);
        await SeedAcceptedAsync(services, a, b);

        var sources = await services.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Contains(b, sources.Friends);
        Assert.False(await redis.Database.KeyExistsAsync(CacheKey(a)));
    }

    [Fact]
    public async Task Redis_chet_thi_doc_DB_va_log_canh_bao()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var logs = new CollectingLoggerProvider();
        var cs = await postgres.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddSocialGraphModule(cs)
            .AddSharedKernelRedis(ApiFactory.UnreachableRedis)
            .AddLogging(b => b.AddProvider(logs))
            .BuildServiceProvider();
        await services.MigrateSocialGraphModuleAsync();
        await SeedAcceptedAsync(services, a, b);

        var sources = await services.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Contains(b, sources.Friends);
        Assert.Contains(
            logs.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("fail-open", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ket_qua_rong_van_duoc_cache()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var harness = await MigratedWithRedisAsync();
        await using var services = harness.Services;

        var reader = services.GetRequiredService<IFeedSourceReader>();
        Assert.True((await reader.GetAsync(a)).IsEmpty);
        Assert.True(await redis.Database.KeyExistsAsync(CacheKey(a)), "người mới (rỗng) phải được cache");

        await SeedAcceptedAsync(services, a, b);
        var stillCached = await reader.GetAsync(a);
        Assert.True(stillCached.IsEmpty, "cache rỗng bị bỏ qua — coi miss rồi đọc DB");
        Assert.DoesNotContain(b, stillCached.Friends);
    }

    private async Task<(ServiceProvider Services, string ConnectionString)> MigratedWithRedisAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection()
            .AddSocialGraphModule(cs)
            .AddSharedKernelRedis(redis.ConnectionString)
            .AddLogging()
            .BuildServiceProvider();
        await services.MigrateSocialGraphModuleAsync();
        await WaitForRedisAsync(services);
        return (services, cs);
    }

    private static async Task WaitForRedisAsync(ServiceProvider services)
    {
        // ServiceCollection trần không chạy RedisConnectionStarter — GetAsync mới bắt đầu kết nối.
        var connection = await services.GetRequiredService<RedisConnection>().GetAsync();
        Assert.True(connection.IsConnected, "RedisFixture chạy mà RedisConnection của app chưa nối được");
    }

    private static async Task SeedAcceptedAsync(ServiceProvider services, Guid a, Guid b)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();
        var row = Friendship.Request(a, b, Now);
        row.Status = FriendshipStatus.Accepted;
        row.AcceptedAt = Now;
        db.Friendships.Add(row);
        await db.SaveChangesAsync();
    }

    private static string CacheKey(Guid userId) => $"sg:feed-sources:{userId:D}";

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
