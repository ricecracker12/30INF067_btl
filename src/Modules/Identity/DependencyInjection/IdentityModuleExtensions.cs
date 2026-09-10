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
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào
    /// namespace Infrastructure chỉ để lấy một hằng số (ADR-001: host chỉ biết bề mặt DI của module).
    /// </summary>
    public const string Schema = IdentityDbContext.Schema;

    // Định danh nhóm Swagger KHÔNG nằm ở đây mà ở Presentation/IdentityApiGroup.cs — nó là từ vựng
    // HTTP, thuộc tầng sở hữu HTTP. Bề mặt DI chỉ nói về việc ráp dịch vụ.

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở IdentityDbContextOptions — dùng chung với
        // design-time factory để hai đường không lệch nhau.
        => services.AddDbContext<IdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));

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
