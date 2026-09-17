using Microsoft.EntityFrameworkCore;

namespace SocialApp.Modules.Identity.Infrastructure;

/// <summary>
/// Cấu hình Npgsql của module, đặt ở MỘT chỗ duy nhất vì có hai đường tạo
/// <see cref="IdentityDbContext"/>: DI lúc chạy (AddIdentityModule) và design-time lúc
/// <c>dotnet ef migrations add</c> (<see cref="DesignTimeIdentityDbContextFactory"/>).
///
/// Hai đường mà cấu hình lệch nhau là lỗi câm: migration sinh ra ở design-time sẽ ghi lịch sử vào
/// một bảng khác với bảng runtime đọc, nên EF tưởng migration chưa chạy và áp lại từ đầu.
/// </summary>
public static class IdentityDbContextOptions
{
    /// <summary>Tên bảng lịch sử migration — để trong schema riêng của module, không dùng "public".</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static DbContextOptionsBuilder UseIdentityNpgsql(
        this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable, IdentityDbContext.Schema));
}
