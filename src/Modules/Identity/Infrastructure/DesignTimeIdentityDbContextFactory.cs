using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SocialApp.SharedKernel.Configuration;

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
/// Chuỗi kết nối: biến môi trường <c>ConnectionStrings__Postgres</c> nếu có (trỏ vào DB khác — DB tạm,
/// staging); không thì Postgres của compose dev qua localhost, mật khẩu đọc từ <c>deploy/.env</c>
/// (<see cref="DevEnvFile"/>) — ở máy dev không cần đặt gì, nhưng BẮT BUỘC có file: thiếu thì MỌI lệnh
/// <c>dotnet ef</c>, kể cả <c>migrations add</c> vốn không mở kết nối, từ chối chạy với thông báo chỉ cách sửa.
/// </summary>
public sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        // Thư mục hiện hành khi chạy dotnet ef nằm trong repo, nên DevEnvFile đi ngược lên được tới deploy/.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DevEnvFile.LocalPostgresConnectionString(Directory.GetCurrentDirectory());

        // UseIdentityNpgsql trả về builder không generic; giữ biến generic để .Options ra đúng
        // DbContextOptions<IdentityDbContext> mà constructor của context đòi.
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.UseIdentityNpgsql(connectionString);

        return new IdentityDbContext(builder.Options);
    }
}
