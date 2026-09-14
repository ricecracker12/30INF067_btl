using StackExchange.Redis;
using Testcontainers.Redis;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Redis thật cho test thu hồi token (D8). Dùng làm <c>IClassFixture</c>: MỘT container cho mỗi lớp cần nó — hiện chỉ
/// <c>TokenRevocationTests</c>, nên không dựng collection riêng. <see cref="Database"/> là client RIÊNG của test, không phải kết
/// nối của app: test ghi/đọc key bằng tay để kiểm bên đọc và bên ghi độc lập nhau.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ConnectionMultiplexer? _client;

    public string ConnectionString => _container.GetConnectionString();

    public IDatabase Database => (_client ?? throw new InvalidOperationException("RedisFixture chưa khởi động.")).GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        await _container.DisposeAsync();
    }
}
