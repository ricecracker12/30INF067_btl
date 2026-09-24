using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SocialApp.SharedKernel.Configuration;

namespace SocialApp.Modules.Messaging.Infrastructure;

/// <summary>
/// Đường tạo context cho EF tools lúc thiết kế (<c>dotnet ef migrations add</c>). Không dùng lúc chạy. Bản sao đúng khuôn của
/// <c>DesignTimeSocialGraphDbContextFactory</c> — module tự làm startup project cho chính nó (ADR-001).
///
/// Lệnh (chạy từ gốc repo — AGENTS.md Mục 13):
///   dotnet ef migrations add &lt;Tên&gt; \
///     --project src/backend/Modules/Messaging/SocialApp.Modules.Messaging.csproj \
///     --startup-project src/backend/Modules/Messaging/SocialApp.Modules.Messaging.csproj \
///     --output-dir Infrastructure/Migrations
/// </summary>
public sealed class DesignTimeMessagingDbContextFactory : IDesignTimeDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DevEnvFile.LocalPostgresConnectionString(Directory.GetCurrentDirectory());

        var builder = new DbContextOptionsBuilder<MessagingDbContext>();
        builder.UseMessagingNpgsql(connectionString);

        return new MessagingDbContext(builder.Options);
    }
}
