using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.Profile.Infrastructure;
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
