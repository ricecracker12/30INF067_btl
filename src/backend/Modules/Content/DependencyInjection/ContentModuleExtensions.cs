using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.Content.Application.Media;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Infrastructure;
using SocialApp.Modules.Content.Infrastructure.Cleanup;
using SocialApp.Modules.Content.Infrastructure.Persistence;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Content. Api chỉ gọi <see cref="AddContentModule"/>; mọi chi tiết
/// EF nằm lại trong module (ADR-001).
///
/// Mỏng như bản Profile và vì cùng một lý do: GĐ2 KHÔNG seed gì (Mục 5). Kho lưu trữ R2, worker dọn rác
/// và các service của khối C/D đăng ký vào chính hàm này khi tới lượt — đừng mở hàm thứ hai.
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

        // GĐ4 ĐỔI ĐÚNG DÒNG NÀY sang hiện thực thật của module SocialGraph và không chạm gì khác trong
        // module Content (Đ-2.9, Mục 7.4). Tới lúc đó, nếu thấy mình đang sửa file khác trong Content để
        // bật kết bạn thì contract này đã bị đi vòng.
        //
        // Cho tới lúc đó: bài để chế độ "friends" chỉ chính tác giả xem được — đó là hành vi ĐÃ CHỐT của
        // GĐ2, không phải thiếu sót. Singleton vì AlwaysStrangers không giữ trạng thái gì.
        services.AddSingleton<IFriendshipReader, AlwaysStrangers>();

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

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service `migrate` one-shot lúc deploy), KHÔNG chạy khi api khởi động
    /// (AGENTS.md Mục 13). Chỉ apply migration: Content không có dữ liệu nền (Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy
    /// phải thoát khác 0 để `set -e` ở CD dừng TRƯỚC `up -d`.
    /// </summary>
    public static async Task MigrateContentModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
