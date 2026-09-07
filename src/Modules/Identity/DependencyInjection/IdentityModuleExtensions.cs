using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;

namespace SocialApp.Modules.Identity.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Identity. Api chỉ gọi <see cref="AddIdentityModule"/>; mọi chi
/// tiết EF nằm lại trong module (ADR-001).
/// </summary>
public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                // Migration history riêng schema — tránh tranh chấp khi GĐ2 thêm context thứ hai.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service `migrate` one-shot lúc deploy), KHÔNG chạy khi api khởi động.
    /// GĐ1 khối A nối thêm vào đây: seeder idempotent → kiểm tra vai trò hệ thống (Mục 5.5).
    /// </summary>
    public static async Task MigrateIdentityModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
