using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Application.Handlers;
using SocialApp.Modules.Notification.Infrastructure;
using SocialApp.Modules.Notification.Infrastructure.Persistence;
using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.Notification.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Notification. Api chỉ gọi <see cref="AddNotificationModule"/>; mọi chi tiết EF nằm lại trong
/// module (ADR-001). D9–D11 (store, handler, controller) và C6 (pusher) đăng ký vào ĐÚNG hàm này — đừng mở hàm thứ hai.
///
/// Các dòng ở đây phải dựng được bằng <c>new ServiceCollection()</c> KHÔNG host: <c>PostgresFixture</c> và
/// <c>NotificationDbContextSchemaTests</c> làm vậy. Thứ gì cần IConfiguration/IHostEnvironment thì nhận qua tham số.
/// </summary>
public static class NotificationModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào namespace Infrastructure chỉ để
    /// lấy một hằng số.
    /// </summary>
    public const string Schema = NotificationDbContext.Schema;

    public static IServiceCollection AddNotificationModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở NotificationDbContextOptions — dùng chung với design-time factory.
        services.AddDbContext<NotificationDbContext>(options => options.UseNotificationNpgsql(connectionString));

        // Một đồng hồ cho cả process — các module khác cũng TryAdd dòng này.
        services.TryAddSingleton(TimeProvider.System);

        // D9 (Đ-6.16): upsert gộp — chỗ duy nhất ghi thông báo. Scoped vì giữ NotificationDbContext; handler D10 chạy mỗi lượt một scope.
        services.AddScoped<INotificationStore, NotificationStore>();

        // D10 (Đ-6.17): ba loại "làm ngay". CHỈ qua AddIntegrationEventHandler (C0) — AddScoped<IIntegrationEventHandler<…>> compile được
        // nhưng bus không bao giờ gọi. Bus đọc danh sách đăng ký lúc dựng; container trần của test schema không dựng bus nên không sao.
        // comment/reply/reaction (GĐ3) và message (GĐ5) thêm ở bước 9 — commit riêng, mỗi handler một ca đi từ API thật.
        services.AddIntegrationEventHandler<FriendRequestSent, FriendRequestSentHandler>();
        services.AddIntegrationEventHandler<FriendRequestAccepted, FriendRequestAcceptedHandler>();
        services.AddIntegrationEventHandler<ContentHidden, ContentHiddenHandler>();

        // CHỈ đăng ký validator của module. KHÔNG gọi AddFluentValidationAutoValidation ở đây: cấu hình MVC toàn cục, host
        // đã gọi một lần. Chưa có validator nào tới D11 — dòng này không tốn gì khi assembly rỗng.
        services.AddValidatorsFromAssembly(typeof(NotificationModuleExtensions).Assembly, ServiceLifetime.Singleton);

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service <c>migrate</c> one-shot lúc deploy), KHÔNG chạy khi api khởi động (AGENTS.md
    /// Mục 13). Chỉ apply migration: Notification không có dữ liệu nền (giai-doan-6.md Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy phải thoát khác 0 để
    /// <c>set -e</c> ở CD dừng TRƯỚC <c>up -d</c>.
    /// </summary>
    public static async Task MigrateNotificationModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
