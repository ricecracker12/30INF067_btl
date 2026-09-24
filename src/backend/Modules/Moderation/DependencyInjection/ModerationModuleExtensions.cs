using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.Modules.Moderation.Infrastructure;
using SocialApp.Modules.Moderation.Infrastructure.Audit;
using SocialApp.Modules.Moderation.Infrastructure.Persistence;
using SocialApp.SharedKernel.Audit;

namespace SocialApp.Modules.Moderation.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Moderation. Api chỉ gọi <see cref="AddModerationModule"/>; mọi chi tiết EF nằm lại trong
/// module (ADR-001). C1 (<c>IAuditTrail</c>) và D6–D8 đăng ký vào ĐÚNG hàm này — đừng mở hàm thứ hai.
///
/// Các dòng ở đây phải dựng được bằng <c>new ServiceCollection()</c> KHÔNG host: <c>PostgresFixture</c> và
/// <c>ModerationDbContextSchemaTests</c> làm vậy. Thứ gì cần IConfiguration/IHostEnvironment thì nhận qua tham số.
/// </summary>
public static class ModerationModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào namespace Infrastructure chỉ để
    /// lấy một hằng số.
    /// </summary>
    public const string Schema = ModerationDbContext.Schema;

    public static IServiceCollection AddModerationModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở ModerationDbContextOptions — dùng chung với design-time factory.
        services.AddDbContext<ModerationDbContext>(options => options.UseModerationNpgsql(connectionString));

        // Một đồng hồ cho cả process — các module khác cũng TryAdd dòng này.
        services.TryAddSingleton(TimeProvider.System);

        // C1 (Đ-6.3, Đ-6.15): hợp đồng ghi audit. Scoped vì giữ ModerationDbContext (đường tx == null). IHttpContextAccessor để
        // lấy IP người thao tác — AddHttpContextAccessor là TryAdd, gọi nhiều lần vô hại.
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditTrail, SqlAuditTrail>();

        // D6 (Đ-6.12): POST /reports. IModerationTargets do AddSharedKernel (composite) + module chủ (provider) đăng ký — container
        // trần của test schema không resolve service này nên không cần chúng.
        services.AddScoped<IReportStore, ReportStore>();
        services.AddScoped<ReportSubmissionService>();

        // CHỈ đăng ký validator của module. KHÔNG gọi AddFluentValidationAutoValidation ở đây: cấu hình MVC toàn cục, host
        // đã gọi một lần.
        services.AddValidatorsFromAssembly(typeof(ModerationModuleExtensions).Assembly, ServiceLifetime.Singleton);

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service <c>migrate</c> one-shot lúc deploy), KHÔNG chạy khi api khởi động (AGENTS.md
    /// Mục 13). Chỉ apply migration: Moderation không có dữ liệu nền (giai-doan-6.md Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy phải thoát khác 0 để
    /// <c>set -e</c> ở CD dừng TRƯỚC <c>up -d</c>.
    /// </summary>
    public static async Task MigrateModerationModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
