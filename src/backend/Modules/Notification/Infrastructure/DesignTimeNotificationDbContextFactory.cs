using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SocialApp.SharedKernel.Configuration;

namespace SocialApp.Modules.Notification.Infrastructure;

/// <summary>
/// Đường tạo context cho EF tools lúc thiết kế (<c>dotnet ef migrations add</c>). Không dùng lúc chạy. Bản sao đúng khuôn của
/// <c>DesignTimeSocialGraphDbContextFactory</c> — module tự làm startup project cho chính nó (ADR-001).
///
/// Lệnh (chạy từ gốc repo — xem AGENTS.md Mục 13):
///   dotnet ef migrations add &lt;Tên&gt; \
///     --project src/backend/Modules/Notification/SocialApp.Modules.Notification.csproj \
///     --startup-project src/backend/Modules/Notification/SocialApp.Modules.Notification.csproj \
///     --output-dir Infrastructure/Migrations
/// </summary>
public sealed class DesignTimeNotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        // Thư mục hiện hành khi chạy dotnet ef nằm trong repo, nên DevEnvFile đi ngược lên được tới deploy/.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DevEnvFile.LocalPostgresConnectionString(Directory.GetCurrentDirectory());

        var builder = new DbContextOptionsBuilder<NotificationDbContext>();
        builder.UseNotificationNpgsql(connectionString);

        return new NotificationDbContext(builder.Options);
    }
}
