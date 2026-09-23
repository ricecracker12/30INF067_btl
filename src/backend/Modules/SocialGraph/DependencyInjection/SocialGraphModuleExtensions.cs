using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.SocialGraph.Application;
using SocialApp.Modules.SocialGraph.Application.Relationships;
using SocialApp.Modules.SocialGraph.Infrastructure;
using SocialApp.Modules.SocialGraph.Infrastructure.Persistence;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.SocialGraph.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module SocialGraph. Api chỉ gọi <see cref="AddSocialGraphModule"/>; mọi chi tiết
/// EF nằm lại trong module (ADR-001).
///
/// A5 đăng ký <c>IFriendshipReader</c> + <c>IFeedSourceReader</c> vào ĐÚNG hàm này. C1, D0 cũng vậy —
/// đừng mở hàm thứ hai. SocialGraph không có dữ liệu nền (Mục 5).
/// </summary>
public static class SocialGraphModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào
    /// namespace Infrastructure chỉ để lấy một hằng số (ADR-001: host chỉ biết bề mặt DI của module).
    /// </summary>
    public const string Schema = SocialGraphDbContext.Schema;

    public static IServiceCollection AddSocialGraphModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở SocialGraphDbContextOptions — dùng chung với
        // design-time factory để hai đường không lệch nhau.
        services.AddDbContext<SocialGraphDbContext>(options => options.UseSocialGraphNpgsql(connectionString));

        // Đ-4.3: hiện thực THẬT của BR-02. Dòng AlwaysStrangers ở AddContentModule đã bị XÓA — không phải bị đè.
        // Đăng ký hai lần thì cái sau thắng im lặng, và thứ tự hai dòng Add*Module trong Program.cs quyết định
        // BR-02 thật hay giả. Scoped vì đọc qua SocialGraphDbContext.
        services.AddScoped<IFriendshipReader, FriendshipReader>();
        services.AddScoped<IFeedSourceReader, FeedSourceReader>();

        // C1 (Đ-4.8, Q-C2): bind có điều kiện — có IConfiguration (host) thì đọc Feed:SourceCache:Enabled,
        // ServiceCollection trần không có thì mặc định bật. BindConfiguration đòi IConfiguration và làm
        // FeedSourceReaderTests ném lúc resolve. RedisConnection do HOST đăng ký (AddSharedKernelRedis);
        // test trần resolve reader thì thêm dòng đó (L12: cổng 1 = fail-open).
        services.AddOptions<FeedSourceCacheOptions>()
            .Configure<IServiceProvider>((o, sp) =>
                sp.GetService<IConfiguration>()?.GetSection(FeedSourceCacheOptions.Section).Bind(o));
        services.AddSingleton<IFeedSourceCache, FeedSourceCache>();

        // D0. Các dòng dưới đây phải dựng được bằng `new ServiceCollection()` KHÔNG host: PostgresFixture,
        // SocialGraphDbContextSchemaTests, FriendshipReaderTests, FeedSourceReaderTests đều làm vậy. Thứ gì
        // cần IConfiguration/IHostEnvironment thì nhận qua tham số, đừng đọc ở đây. Resolve
        // IFeedSourceReader / IFeedSourceCache thì cần RedisConnection — đó là việc của L12, không của hàm này.

        // Đồng hồ của service khối D (created_at/updated_at/accepted_at). TryAdd vì AddProfileModule /
        // AddContentModule cũng gọi dòng này — một đồng hồ cho cả process.
        services.TryAddSingleton(TimeProvider.System);

        // CHỈ đăng ký validator của module. KHÔNG gọi AddFluentValidationAutoValidation ở đây: đó là cấu hình
        // MVC toàn cục, host đã gọi một lần — gọi lại là mỗi lỗi validate hiện hai lần trong `errors`.
        services.AddValidatorsFromAssembly(typeof(SocialGraphModuleExtensions).Assembly, ServiceLifetime.Singleton);

        // Event Đ-4.15: phát qua IEventPublisher (singleton, AddSharedKernel), không giữ trạng thái, không chạm DbContext — Singleton.
        services.AddSingleton<SocialGraphEvents>();

        // Scoped vì RelationshipStore sẽ giữ SocialGraphDbContext (scoped); RelationshipService theo cùng
        // vòng đời của thứ nó cầm. IUserDirectory do HOST + AddProfileModule đăng ký — chỗ trần không
        // resolve RelationshipService nên thiếu IUserDirectory ở đó không sao.
        services.AddScoped<IRelationshipStore, RelationshipStore>();
        services.AddScoped<RelationshipService>();

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service <c>migrate</c> one-shot lúc deploy), KHÔNG chạy khi api khởi động
    /// (AGENTS.md Mục 13). Chỉ apply migration: SocialGraph không có dữ liệu nền (Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy
    /// phải thoát khác 0 để <c>set -e</c> ở CD dừng TRƯỚC <c>up -d</c>.
    /// </summary>
    public static async Task MigrateSocialGraphModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
