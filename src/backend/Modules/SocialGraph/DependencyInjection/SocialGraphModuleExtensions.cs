using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.SocialGraph.Infrastructure;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.SocialGraph.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module SocialGraph. Api chỉ gọi <see cref="AddSocialGraphModule"/>; mọi chi tiết
/// EF nằm lại trong module (ADR-001).
///
/// A5 đăng ký <c>IFriendshipReader</c> + <c>IFeedSourceReader</c> vào ĐÚNG hàm này. C1, D0 cũng vậy —
/// đừng mở hàm thứ hai. SocialGraph không có dữ liệu nền (Mục 5).
/// </summary>
public static class SocialGraphModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào
    /// namespace Infrastructure chỉ để lấy một hằng số (ADR-001: host chỉ biết bề mặt DI của module).
    /// </summary>
    public const string Schema = SocialGraphDbContext.Schema;

    public static IServiceCollection AddSocialGraphModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở SocialGraphDbContextOptions — dùng chung với
        // design-time factory để hai đường không lệch nhau.
        services.AddDbContext<SocialGraphDbContext>(options => options.UseSocialGraphNpgsql(connectionString));

        // Đ-4.3: hiện thực THẬT của BR-02. Dòng AlwaysStrangers ở AddContentModule đã bị XÓA — không phải bị đè.
        // Đăng ký hai lần thì cái sau thắng im lặng, và thứ tự hai dòng Add*Module trong Program.cs quyết định
        // BR-02 thật hay giả. Scoped vì đọc qua SocialGraphDbContext.
        services.AddScoped<IFriendshipReader, FriendshipReader>();
        services.AddScoped<IFeedSourceReader, FeedSourceReader>();

        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service <c>migrate</c> one-shot lúc deploy), KHÔNG chạy khi api khởi động
    /// (AGENTS.md Mục 13). Chỉ apply migration: SocialGraph không có dữ liệu nền (Mục 5).
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: migration hỏng thì bước deploy
    /// phải thoát khác 0 để <c>set -e</c> ở CD dừng TRƯỚC <c>up -d</c>.
    /// </summary>
    public static async Task MigrateSocialGraphModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
