using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Redis;

/// <summary>
/// Health check trên CHÍNH <see cref="RedisConnection"/> của app. Thay <c>AspNetCore.HealthChecks.Redis</c>: gói đó hoặc mở kết nối
/// riêng, hoặc nhận multiplexer qua factory ĐỒNG BỘ — với kết nối mở ở nền thì phải chặn luồng chờ, và <c>/health/ready</c> đứng
/// vài giây mỗi lần gọi khi Redis chết.
/// </summary>
internal sealed class RedisConnectionHealthCheck(RedisConnection redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (redis.ConnectedOrNull() is not { } connection)
            return new HealthCheckResult(context.Registration.FailureStatus, "Chưa có kết nối Redis");

        try
        {
            await connection.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis không trả lời PING", ex);
        }
    }
}
