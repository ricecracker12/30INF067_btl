using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Redis;

namespace SocialApp.SharedKernel.Authentication;

public static class TokenRevocationExtensions
{
    /// <summary>
    /// <see cref="ITokenRevocationStore"/> trên Redis (D8), dùng kết nối chung của <see cref="RedisExtensions.AddSharedKernelRedis"/>.
    /// Cần <c>IOptions&lt;JwtOptions&gt;</c> do host đăng ký sau khi validate.
    /// </summary>
    public static IServiceCollection AddSharedKernelTokenRevocation(this IServiceCollection services)
    {
        // Thiếu kết nối chung thì chết NGAY lúc khởi động, không đợi request có token đầu tiên nhận 500.
        if (!services.Any(d => d.ServiceType == typeof(RedisConnection)))
            throw new InvalidOperationException(
                $"Gọi {nameof(RedisExtensions.AddSharedKernelRedis)} trước {nameof(AddSharedKernelTokenRevocation)}.");

        services.AddSingleton<ITokenRevocationStore, RedisTokenRevocationStore>();
        return services;
    }
}
