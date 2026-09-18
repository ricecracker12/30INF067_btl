using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.Content.Infrastructure;

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
