using Microsoft.EntityFrameworkCore;

namespace SocialApp.Modules.Content.Infrastructure;

/// <summary>
/// Cấu hình Npgsql của module, đặt ở MỘT chỗ duy nhất vì có hai đường tạo
/// <see cref="ContentDbContext"/>: DI lúc chạy (AddContentModule) và design-time lúc
/// <c>dotnet ef migrations add</c> (<see cref="DesignTimeContentDbContextFactory"/>).
///
/// Hai đường mà cấu hình lệch nhau là lỗi câm: migration sinh ra ở design-time sẽ ghi lịch sử vào
/// một bảng khác với bảng runtime đọc, nên EF tưởng migration chưa chạy và áp lại từ đầu.
/// </summary>
public static class ContentDbContextOptions
{
    /// <summary>
    /// Tên bảng lịch sử migration — để trong schema riêng của module, không dùng "public". Cùng tên
    /// <c>__EFMigrationsHistory</c> ở ba schema khác nhau là đúng khuôn Đ-2.1, không phải trùng lặp.
    /// </summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static DbContextOptionsBuilder UseContentNpgsql(
        this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable, ContentDbContext.Schema));
}
