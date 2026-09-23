using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Application.Media;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Infrastructure;
using SocialApp.Modules.Content.Infrastructure.Cleanup;
using SocialApp.Modules.Content.Infrastructure.Feed;
using SocialApp.Modules.Content.Infrastructure.Moderation;
using SocialApp.Modules.Content.Infrastructure.Persistence;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Content.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Content. Api chỉ gọi <see cref="AddContentModule"/>; mọi chi tiết
/// EF nằm lại trong module (ADR-001).
///
/// Mỏng như bản Profile và vì cùng một lý do: GĐ2 KHÔNG seed gì (Mục 5). Kho lưu trữ R2, worker dọn rác
/// và các service của khối C/D đăng ký vào chính hàm này khi tới lượt — đừng mở hàm thứ hai.
///
/// Content tiêu thụ <c>IUserDirectory</c> (Profile đăng ký) và <c>IFriendshipReader</c> (SocialGraph đăng ký) —
/// thiếu module chủ trong host thì request đầu tiên đọc bài nổ lúc resolve.
/// </summary>
public static class ContentModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào
    /// namespace Infrastructure chỉ để lấy một hằng số (ADR-001: host chỉ biết bề mặt DI của module).
    /// </summary>
    public const string Schema = ContentDbContext.Schema;

    public static IServiceCollection AddContentModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở ContentDbContextOptions — dùng chung với
        // design-time factory để hai đường không lệch nhau.
        services.AddDbContext<ContentDbContext>(options => options.UseContentNpgsql(connectionString));

        // C4 (Đ-2.13): worker dọn rác media. Đăng ký LUÔN, kiểm công tắc BÊN TRONG worker — đăng ký có điều kiện thì cấu hình
        // sai im lặng, còn kiểm bên trong thì log được một dòng "đang tắt" lúc khởi động. Mặc định tắt (Q-C2); staging bật bằng
        // Media__Cleanup__Enabled=true. BindConfiguration đọc IConfiguration của host lúc resolve — ServiceCollection trần của
        // test (PostgresFixture) không resolve options này nên không cần IConfiguration.
        services.AddOptions<MediaCleanupOptions>().BindConfiguration(MediaCleanupOptions.Section);
        services.AddHostedService<MediaCleanupWorker>();

        // D0. Hai dòng dưới đây phải dựng được bằng `new ServiceCollection()` KHÔNG host: PostgresFixture
        // (SeededContentDatabaseAsync) và ContentDbContextSchemaTests đều làm vậy. Thứ gì cần IConfiguration/
        // IHostEnvironment thì nhận qua tham số, hoặc bind lười như MediaCleanupOptions ở ngay trên.

        // Đồng hồ của service khối D (created_at/updated_at/edited_at). TryAdd vì AddProfileModule cũng gọi dòng này —
        // một đồng hồ cho cả process, module nào chạy trước không quan trọng.
        services.TryAddSingleton(TimeProvider.System);

        // CHỈ đăng ký validator của module. KHÔNG gọi AddFluentValidationAutoValidation ở đây: đó là cấu hình MVC toàn
        // cục, host đã gọi một lần — gọi lại là mỗi lỗi validate hiện hai lần trong `errors`.
        services.AddValidatorsFromAssembly(typeof(ContentModuleExtensions).Assembly, ServiceLifetime.Singleton);

        // D4. Singleton, KHÁC ProfileService (scoped): UploadTicketService không chạm DbContext — nó chỉ ký HMAC cục bộ
        // bằng IObjectStorage, thứ đã là singleton (R2StorageExtensions). Vòng đời theo thứ nó cầm, không theo thói quen.
        // Chỉ ĐĂNG KÝ nên dòng này vẫn dựng trần được bằng `new ServiceCollection()`; chỗ trần không resolve nó nên thiếu
        // IObjectStorage ở đó không sao — cùng lập luận với ProfileService trong AddProfileModule.
        services.AddSingleton<UploadTicketService>();

        // D5. Scoped vì PostStore giữ ContentDbContext (scoped); PostService theo cùng vòng đời của thứ nó cầm.
        // PostResponseMapper cũng scoped dù chỉ cầm IObjectStorage (singleton): nó là cộng tác viên của PostService và
        // D6/D7 sẽ dùng chung cùng một instance trong một request. IObjectStorage và IUserDirectory do HOST và module
        // Profile đăng ký — AddContentModule vẫn dựng trần được vì bốn lớp test ở Mục 1.3 luật 1 không resolve chúng.
        services.AddScoped<IPostStore, PostStore>();
        services.AddScoped<PostResponseMapper>();
        services.AddScoped<PostService>();

        // D6. Đường ĐỌC tách khỏi đường ghi: hai service không dùng chung phụ thuộc nào ngoài store và mapper.
        services.AddScoped<PostReadService>();

        // C3 (GĐ4). Scoped theo thứ nó cầm (IPostStore). Một chỗ dựng PostResponse cho mọi danh sách — GĐ3 cắm myReaction ở đây.
        services.AddScoped<PostHydrator>();

        // C2 (GĐ4). Scoped vì FeedStore giữ ContentDbContext.
        services.AddScoped<IFeedStore, FeedStore>();

        // C2 (GĐ6, Đ-6.3): provider BÀI của hợp đồng ghi IModerationTargets — Moderation ẩn/khôi phục bài trong transaction của
        // nó, SQL vẫn do Content viết. Bình luận: một dòng AddScoped<IModerationTargetProvider, …> nữa sau khi GĐ3 merge.
        // Resolve cần IFriendshipReader (SocialGraph đăng ký) — chỗ trần không resolve provider nên không sao.
        services.AddScoped<IModerationTargetProvider, ContentModerationTargets>();

        // C4 (GĐ4, Đ-4.8, Q-C2): công tắc bind có điều kiện — có IConfiguration (host) thì đọc Feed:PageCache:Enabled,
        // ServiceCollection trần thì mặc định bật. Cùng khuôn FeedSourceCacheOptions của SocialGraph. Singleton vì chỉ cầm
        // RedisConnection (singleton, HOST đăng ký bằng AddSharedKernelRedis). Chỗ dựng trần nào resolve PostService,
        // FeedService hay IFeedPageCache thì thêm AddSharedKernelRedis (cổng 1 = fail-open) — cùng luật L12 của C1.
        services.AddOptions<FeedPageCacheOptions>()
            .Configure<IServiceProvider>((o, sp) =>
                sp.GetService<IConfiguration>()?.GetSection(FeedPageCacheOptions.Section).Bind(o));
        services.AddSingleton<IFeedPageCache, RedisFeedPageCache>();
        services.AddScoped<FeedService>();

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service <c>migrate</c> one-shot lúc deploy), KHÔNG chạy khi api khởi động
    /// (AGENTS.md Mục 13). Chỉ apply migration: Content không có dữ liệu nền (Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy
    /// phải thoát khác 0 để <c>set -e</c> ở CD dừng TRƯỚC <c>up -d</c>.
    /// </summary>
    public static async Task MigrateContentModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
