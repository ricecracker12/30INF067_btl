using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace SocialApp.IntegrationTests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dùng chung cho test dựng cả app mà KHÔNG chạm Postgres /
/// Redis (smoke, hợp đồng API). Mọi test như vậy dùng lớp này, không dùng thẳng
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
///
/// Vì sao phải khai báo chuỗi kết nối tường minh: ở Development, thiếu chuỗi kết nối thì Program.cs dựng nó
/// từ <c>deploy/.env</c> và BẮT BUỘC có file đó (AGENTS.md Mục 13). CI không có <c>deploy/.env</c>
/// (gitignore) — nên test phải tự nói nó chạy với cấu hình gì, thay vì dựa vào file trên máy người chạy.
///
/// Hai chuỗi trỏ vào cổng 1 trên loopback, cố ý không có dịch vụ nào ở đó: test nào vô tình chạm DB/Redis sẽ
/// đỏ ngay, thay vì âm thầm dựa vào Postgres đang chạy trên máy dev rồi đỏ trên CI. Không có mật khẩu nào.
/// Test cần DB thật thì dùng Testcontainers (xem IdentityDbContextSchemaTests), không dùng lớp này.
///
/// Môi trường Development vì Swagger chỉ bật ở Development + Staging (IdentityContractTests cần nó), và
/// Staging trong test thì fail-fast theo đúng thiết kế (StartupConfigurationTests).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=khong_ton_tai;Username=khong_ton_tai";
    public const string UnreachableRedis = "127.0.0.1:1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Postgres", UnreachablePostgres);
        builder.UseSetting("ConnectionStrings:Redis", UnreachableRedis);
    }
}
