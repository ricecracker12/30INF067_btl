using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.Modules.Profile.Infrastructure;
using SocialApp.Modules.Profile.Infrastructure.Persistence;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Profile.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Profile. Api chỉ gọi <see cref="AddProfileModule"/>; mọi chi tiết
/// EF nằm lại trong module (ADR-001).
///
/// Mỏng hơn bản Identity rất nhiều và đó là đúng: Profile KHÔNG có dữ liệu nền nào để seed, không có
/// vai trò hệ thống để kiểm (Mục 5 của kế hoạch GĐ2 — GĐ2 không seed gì). Đừng "cho đối xứng" bằng
/// một seeder rỗng: nó là một chỗ trống không ai biết để làm gì.
/// </summary>
public static class ProfileModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào
    /// namespace Infrastructure chỉ để lấy một hằng số (ADR-001: host chỉ biết bề mặt DI của module).
    /// </summary>
    public const string Schema = ProfileDbContext.Schema;

    public static IServiceCollection AddProfileModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở ProfileDbContextOptions — dùng chung với
        // design-time factory để hai đường không lệch nhau.
        services.AddDbContext<ProfileDbContext>(options => options.UseProfileNpgsql(connectionString));

        // Cửa duy nhất để module khác đọc hồ sơ mà không import module này (Đ-2.3, A6). Module CHỦ đăng ký
        // hiện thực của mình — đúng tiền lệ IRolePermissionSource trong AddIdentityModule. Scoped vì
        // UserDirectory dùng ProfileDbContext.
        //
        // HỆ QUẢ: từ đây module Content chỉ chạy được khi host ĐÃ gọi AddProfileModule. Không có gì bắt lỗi
        // lúc build — thiếu thì nổ lúc resolve service của request đầu tiên. StartupConfigurationTests có
        // một khẳng định canh đúng chuyện đó.
        services.AddScoped<IUserDirectory, UserDirectory>();

        // D0. Ba dòng dưới đây phải dựng được bằng `new ServiceCollection()` KHÔNG host: PostgresFixture
        // (SeededContentDatabaseAsync), ProfileDbContextSchemaTests và UserDirectoryTests đều làm vậy. Thứ gì cần
        // IConfiguration/IHostEnvironment thì nhận qua tham số như AddIdentityEmail, đừng đọc ở đây.

        // Đồng hồ của service khối D (created_at/updated_at). TryAdd: host và test có thể đã đăng ký rồi, và ba module
        // cùng gọi dòng này thì chỉ cái đầu tiên có tác dụng — đúng ý, một đồng hồ cho cả process.
        services.TryAddSingleton(TimeProvider.System);

        // CHỈ đăng ký validator của module. KHÔNG gọi AddFluentValidationAutoValidation ở đây: đó là cấu hình MVC toàn
        // cục, host đã gọi một lần — gọi lại là mỗi lỗi validate hiện hai lần trong `errors`.
        // Singleton như Identity: validator của khối D là hàm thuần trên DTO, không giữ trạng thái, không chạm DbContext.
        services.AddValidatorsFromAssembly(typeof(ProfileModuleExtensions).Assembly, ServiceLifetime.Singleton);

        // D1. Scoped vì ProfileStore giữ ProfileDbContext (scoped); ProfileService theo cùng vòng đời của thứ nó cầm.
        // ProfileService còn cần IObjectStorage để ký avatarUrl (Đ-2.9) — HOST đăng ký cái đó (Program.cs, R2StorageExtensions),
        // không phải module này: khóa R2 là cấu hình của host, và AddProfileModule phải dựng được bằng `new ServiceCollection()`
        // trần (bốn lớp test ở Mục 1.3 luật 1 làm vậy). Hai dòng dưới chỉ ĐĂNG KÝ nên chúng vẫn trần được; chỗ trần không
        // resolve ProfileService nên thiếu IObjectStorage ở đó không sao.
        services.AddScoped<IProfileStore, ProfileStore>();
        services.AddScoped<ProfileService>();

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service `migrate` one-shot lúc deploy), KHÔNG chạy khi api khởi động
    /// (AGENTS.md Mục 13). Chỉ apply migration: Profile không có dữ liệu nền (Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy
    /// phải thoát khác 0 để `set -e` ở CD dừng TRƯỚC `up -d`.
    /// </summary>
    public static async Task MigrateProfileModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
