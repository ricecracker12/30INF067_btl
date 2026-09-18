using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SocialApp.SharedKernel.Configuration;

namespace SocialApp.Modules.Profile.Infrastructure;

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
///     --project src/backend/Modules/Profile/SocialApp.Modules.Profile.csproj \
///     --startup-project src/backend/Modules/Profile/SocialApp.Modules.Profile.csproj \
///     --output-dir Infrastructure/Migrations
///
/// Chuỗi kết nối: biến môi trường <c>ConnectionStrings__Postgres</c> nếu có (trỏ vào DB khác — DB tạm,
/// staging); không thì Postgres của compose dev qua localhost, mật khẩu đọc từ <c>deploy/.env</c>
/// (<see cref="DevEnvFile"/>) — ở máy dev không cần đặt gì, nhưng BẮT BUỘC có file: thiếu thì MỌI lệnh
/// <c>dotnet ef</c>, kể cả <c>migrations add</c> vốn không mở kết nối, từ chối chạy với thông báo chỉ cách sửa.
/// </summary>
public sealed class DesignTimeProfileDbContextFactory : IDesignTimeDbContextFactory<ProfileDbContext>
{
    public ProfileDbContext CreateDbContext(string[] args)
    {
        // Thư mục hiện hành khi chạy dotnet ef nằm trong repo, nên DevEnvFile đi ngược lên được tới deploy/.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DevEnvFile.LocalPostgresConnectionString(Directory.GetCurrentDirectory());

        // UseProfileNpgsql trả về builder không generic; giữ biến generic để .Options ra đúng
        // DbContextOptions<ProfileDbContext> mà constructor của context đòi.
        var builder = new DbContextOptionsBuilder<ProfileDbContext>();
        builder.UseProfileNpgsql(connectionString);

        return new ProfileDbContext(builder.Options);
    }
}
