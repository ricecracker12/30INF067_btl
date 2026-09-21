using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace SocialApp.SharedKernel.Storage;

public static class R2StorageExtensions
{
    /// <summary>
    /// Điểm đăng ký lưu trữ đối tượng DUY NHẤT của app, cạnh <c>AddSharedKernelRedis</c> trong Program.cs — không module
    /// nào tự đăng ký. <paramref name="options"/> đã qua fail-fast của Program.cs (ngoài Development thì chắc chắn đủ).
    ///
    /// Thiếu cấu hình (chỉ xảy ra ở Development, Q-C1) → <see cref="UnconfiguredObjectStorage"/>: khởi động được, dùng
    /// mới ném. Đủ cấu hình → <see cref="R2ObjectStorage"/> (C2). TryAdd để test thay bằng FakeObjectStorage (C5) qua
    /// <c>ConfigureTestServices</c> mà không đụng dòng này.
    ///
    /// Singleton vì worker dọn rác (C4) là hosted service và resolve IObjectStorage ngay lúc host khởi động — hiện thực
    /// nào đứng đây cũng phải dựng được mà KHÔNG gọi mạng (AmazonS3Client chỉ giữ cấu hình + khóa cho tới lời gọi đầu).
    /// </summary>
    public static IServiceCollection AddSharedKernelR2(this IServiceCollection services, R2Options options, string environmentName)
    {
        services.TryAddSingleton(Options.Create(options));

        if (!options.IsComplete)
        {
            services.TryAddSingleton<IObjectStorage>(_ => new UnconfiguredObjectStorage(options, environmentName));
            return services;
        }

        services.TryAddSingleton<IObjectStorage>(_ => new R2ObjectStorage(options));
        return services;
    }
}
