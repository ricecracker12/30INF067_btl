using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SocialApp.SharedKernel.Redis;

/// <summary>
/// Bắt đầu kết nối Redis NGAY lúc host khởi động thay vì đợi request có token đầu tiên resolve <see cref="RedisConnection"/>. Dựng
/// lười thuần túy thì chính request đầu tiên đó thấy "chưa kết nối" và fail-open — không được kiểm thu hồi (thi công D8).
///
/// KHÔNG await trong <see cref="StartAsync"/>: Redis chết thì app vẫn khởi động ngay (Đ-D8). Hệ quả: vài ms đầu sau khởi động vẫn
/// có thể fail-open; đóng hẳn cửa sổ đó là việc của probe khởi động ở load balancer, không phải của app.
/// <c>--migrate</c> không chạy host nên không mở kết nối.
/// </summary>
internal sealed class RedisConnectionStarter(RedisConnection redis, ILogger<RedisConnectionStarter> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = LogFirstAttemptAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    private async Task LogFirstAttemptAsync(CancellationToken stopping)
    {
        try
        {
            var connection = await redis.GetAsync().WaitAsync(stopping);
            if (connection.IsConnected)
                logger.LogInformation("Đã kết nối Redis lúc khởi động");
            else
                logger.LogWarning("Lần kết nối Redis đầu tiên chưa thành công — kiểm tra thu hồi token fail-open, thư viện tự thử lại");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Host dừng trước khi có kết quả — không log vào logger sắp bị dispose.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không mở được kết nối Redis lúc khởi động — kiểm tra thu hồi token fail-open");
        }
    }
}
