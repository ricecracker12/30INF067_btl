using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// Dựng app cho AuthZ matrix. Tách khỏi <see cref="ApiFactory"/> có chủ ý: ApiFactory phục vụ smoke + cổng
/// hợp đồng API và CỐ Ý không chạm DB; matrix cần Postgres thật đã seed ngay từ đầu (Đ3) và cần probe
/// controller. Không có dòng thay thế IRolePermissionSource nào ở đây, và sẽ không bao giờ có (Đ3).
///
/// <see cref="TestJwt.Configure"/> ghi Jwt:* trước khi C4 có người đọc — vô hại, và nhờ vậy C4 không phải
/// sửa factory này.
/// </summary>
public sealed class AuthZApiFactory : WebApplicationFactory<Program>
{
    private string? _postgres;

    /// <summary>Gọi trước CreateClient đầu tiên. Gọi lại với cùng giá trị là vô hại.</summary>
    public void UseDatabase(string connectionString) => _postgres ??= connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Postgres",
            _postgres ?? throw new InvalidOperationException("Gọi UseDatabase trước CreateClient."));
        builder.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
        TestJwt.Configure(builder);

        builder.ConfigureTestServices(services =>
        {
            // Probe controller sống trong assembly TEST: không có trong image, không có trong Swagger,
            // không bị PresentationBoundaryTests quét (nó chỉ quét module + Api).
            services.AddControllers().AddApplicationPart(typeof(AuthZApiFactory).Assembly);
        });
    }
}
