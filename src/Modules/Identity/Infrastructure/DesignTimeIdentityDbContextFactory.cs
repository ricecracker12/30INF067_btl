using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SocialApp.Modules.Identity.Infrastructure;

/// <summary>
/// Đường tạo context cho EF tools lúc thiết kế (<c>dotnet ef migrations add</c>). Không dùng lúc chạy.
///
/// Vì sao cần: EF tools đòi gói <c>Microsoft.EntityFrameworkCore.Design</c> nằm ở startup project.
/// Không có factory này thì startup project buộc phải là SocialApp.Api, tức là host phải kéo EF vào
/// — ngược với ADR-001 (chỉ Infrastructure của module được chạm EF). Có factory thì module tự làm
/// startup project cho chính nó, Api sạch EF, và GĐ2–GĐ6 mỗi module lặp lại đúng khuôn này.
///
/// Lệnh (chạy từ gốc repo — xem AGENTS.md Mục 13):
///   dotnet ef migrations add &lt;Tên&gt; \
///     --project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
///     --startup-project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
///     --output-dir Infrastructure/Migrations
///
/// Chuỗi kết nối ở đây chỉ để EF dựng model; <c>migrations add</c> không mở kết nối nào. Các lệnh có
/// chạm database thật (<c>database update</c>, <c>migrations remove</c>) thì đặt biến môi trường
/// <c>ConnectionStrings__Postgres</c>. Mặc định trỏ vào Postgres của compose dev.
/// </summary>
public sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    private const string DevConnectionString =
        "Host=localhost;Port=5432;Database=socialapp;Username=socialapp;Password=socialapp";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? DevConnectionString;

        // UseIdentityNpgsql trả về builder không generic; giữ biến generic để .Options ra đúng
        // DbContextOptions<IdentityDbContext> mà constructor của context đòi.
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.UseIdentityNpgsql(connectionString);

        return new IdentityDbContext(builder.Options);
    }
}
