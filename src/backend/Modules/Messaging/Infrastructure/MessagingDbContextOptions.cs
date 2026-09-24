using Microsoft.EntityFrameworkCore;

namespace SocialApp.Modules.Messaging.Infrastructure;

/// <summary>
/// Cấu hình Npgsql của module, đặt ở MỘT chỗ duy nhất vì có hai đường tạo <see cref="MessagingDbContext"/>: DI lúc chạy
/// (<c>AddMessagingModule</c>) và design-time lúc <c>dotnet ef migrations add</c> (<see cref="DesignTimeMessagingDbContextFactory"/>).
/// Hai đường lệch nhau là lỗi câm: migration ghi lịch sử vào một bảng khác bảng runtime đọc, EF tưởng chưa chạy và áp lại.
/// </summary>
public static class MessagingDbContextOptions
{
    /// <summary>Tên bảng lịch sử migration — trong schema riêng của module (khuôn Đ-2.1 / Đ-4.1 / Đ-5.1).</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static DbContextOptionsBuilder UseMessagingNpgsql(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable, MessagingDbContext.Schema));
}
