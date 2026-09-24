using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.Messaging.Infrastructure;

namespace SocialApp.Modules.Messaging.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Messaging (khuôn <c>AddSocialGraphModule</c>). Api chỉ gọi <see cref="AddMessagingModule"/>;
/// mọi chi tiết EF nằm lại trong module (ADR-001). Các khối sau (C, D) đăng ký vào ĐÚNG hàm này — đừng mở hàm thứ hai.
///
/// Mọi dòng ở đây phải dựng được bằng <c>new ServiceCollection()</c> KHÔNG host: <c>PostgresFixture</c>,
/// <c>MessagingDbContextSchemaTests</c> làm vậy. Thứ gì cần <c>IConfiguration</c> thì đọc lúc resolve, không đọc ở đây.
/// Messaging không có dữ liệu nền: mã quyền <c>message.send</c> đã có từ GĐ1 (Mục 5).
/// </summary>
public static class MessagingModuleExtensions
{
    /// <summary>Schema Postgres của module, công bố ở tầng DI để host không phải <c>using</c> vào Infrastructure.</summary>
    public const string Schema = MessagingDbContext.Schema;

    public static IServiceCollection AddMessagingModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MessagingDbContext>(options => options.UseMessagingNpgsql(connectionString));

        // Đồng hồ của service (created_at, last_message_at). TryAdd: các module khác cũng gọi — một đồng hồ cho cả process.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service <c>migrate</c> lúc deploy), KHÔNG chạy khi api khởi động (AGENTS.md Mục 13).
    /// Ngoại lệ PHẢI thoát ra ngoài: migration hỏng thì bước deploy thoát khác 0 để CD dừng TRƯỚC <c>up -d</c>.
    /// </summary>
    public static async Task MigrateMessagingModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
