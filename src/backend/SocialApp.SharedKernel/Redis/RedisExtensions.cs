using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Redis;

public static class RedisExtensions
{
    /// <summary>
    /// Điểm đăng ký Redis DUY NHẤT của app: một <see cref="RedisConnection"/> singleton, kết nối bắt đầu lúc host khởi động. Thành
    /// phần nào cần Redis (thu hồi token, health check, GĐ sau: backplane, presence) dùng lại nó, không tự mở kết nối.
    ///
    /// Chuỗi kết nối sai cú pháp thì ném NGAY ở đây; Redis chết thì app vẫn khởi động (<c>AbortOnConnectFail = false</c>) — quên dòng
    /// đó là smoke + cổng hợp đồng đỏ trên CI, vì ApiFactory trỏ Redis không tới được (cạm bẫy 4 của D8).
    /// </summary>
    public static IServiceCollection AddSharedKernelRedis(this IServiceCollection services, string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        // Timeout lệnh ngắn vì người dùng hiện tại là bên đọc thu hồi token — chạy trên MỌI request có token, Redis treo thì chỉ được
        // làm chậm tối đa chừng này. Thêm người dùng cần timeout khác (vd backplane) thì cân nhắc lại con số này.
        options.SyncTimeout = 250;
        options.AsyncTimeout = 250;

        // Factory, không truyền instance: DI chỉ dispose singleton do chính nó dựng → đóng kết nối khi host dừng.
        services.TryAddSingleton(_ => new RedisConnection(options));

        // Mọi chỗ fail-open (thu hồi token, cache feed) dùng chung MỘT bộ giới hạn log cho cả host — đăng ký cạnh kết nối vì
        // ai cần RedisConnection đều đi qua hàm này. TryAdd: module cũng TryAdd TimeProvider.System, một đồng hồ cho cả process.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FailOpenLogThrottle>();
        services.AddHostedService<RedisConnectionStarter>();
        return services;
    }

    /// <summary>Health check Redis trên kết nối chung (<see cref="RedisConnectionHealthCheck"/>). Cần <see cref="AddSharedKernelRedis"/>.</summary>
    public static IHealthChecksBuilder AddSharedKernelRedisCheck(
        this IHealthChecksBuilder builder, string name, IEnumerable<string> tags) =>
        builder.AddCheck<RedisConnectionHealthCheck>(name, tags: tags);
}
