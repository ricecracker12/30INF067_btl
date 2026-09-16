using StackExchange.Redis;

namespace SocialApp.SharedKernel.Redis;

/// <summary>
/// MỘT kết nối Redis cho cả instance — thu hồi token (D8), health check, và từ GĐ sau là backplane/presence/cache. Mỗi thành phần
/// tự mở kết nối riêng thì số kết nối nhân theo số instance khi scale ngang (giới hạn maxclients của Redis).
///
/// Mở bằng <see cref="ConnectionMultiplexer.ConnectAsync(ConfigurationOptions, TextWriter?)"/> ở NỀN, bắt đầu ngay lúc host khởi
/// động (<see cref="RedisConnectionStarter"/>), không chờ: Redis chết thì app vẫn khởi động (Đ-D8). Không dùng
/// <c>ConnectionMultiplexer.Connect</c> đồng bộ: request có token đầu tiên sẽ đứng chờ ~7 giây khi Redis chết (đo ở thi công D8).
/// </summary>
public sealed class RedisConnection : IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connect;

    internal RedisConnection(ConfigurationOptions options) =>
        _connect = new(() => ConnectionMultiplexer.ConnectAsync(options));

    /// <summary>
    /// Multiplexer nếu ĐANG kết nối; chưa xong, kết nối hỏng hay bị rớt → null. Không bao giờ chờ — cho mọi thứ chạy trên đường đi
    /// của request (bên đọc thu hồi token, health check).
    /// </summary>
    public IConnectionMultiplexer? ConnectedOrNull()
    {
        var connect = _connect.Value;
        return connect.IsCompletedSuccessfully && connect.Result.IsConnected ? connect.Result : null;
    }

    /// <summary>
    /// Chờ lần kết nối đầu có kết quả (Redis chết thì có thể vài giây) — cho bên GHI, nơi thà chậm còn hơn mất lệnh. Kết nối hỏng
    /// thì task vẫn xong; lệnh Redis chạy sau đó mới ném.
    /// </summary>
    public Task<ConnectionMultiplexer> GetAsync() => _connect.Value;

    public async ValueTask DisposeAsync()
    {
        if (!_connect.IsValueCreated)
            return;

        var connect = _connect.Value;
        if (connect.IsCompletedSuccessfully)
        {
            await connect.Result.DisposeAsync();
            return;
        }

        // Còn đang kết nối (Redis chết, thư viện đang thử lại): đóng ngay khi task xong, không để multiplexer mồ côi tự thử lại mãi.
        _ = connect.ContinueWith(
            t => t.Result.Dispose(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }
}
