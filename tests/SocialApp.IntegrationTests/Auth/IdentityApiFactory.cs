using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application.Email;
using SocialApp.Modules.Identity.DependencyInjection;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// Dựng app cho test endpoint khối D. Theo khuôn <see cref="AuthZ.AuthZApiFactory"/>, khác ở ba chỗ:
/// <list type="number">
/// <item>Database RIÊNG cho mỗi lớp test, đã migrate + seed: test auth SỬA dữ liệu (luật B1). Email mỗi test sinh
/// ngẫu nhiên nên các test trong cùng lớp không giẫm nhau.</item>
/// <item><see cref="FakeRemoteIpStartupFilter"/> — quên dòng này là test đỏ 429 ngẫu nhiên, trông như flaky (Đ-D7).</item>
/// <item><see cref="IEmailSender"/> thay bằng <see cref="CapturingEmailSender"/>.</item>
/// </list>
/// Redis mặc định không tới được như ApiFactory; D8 truyền Redis thật qua <see cref="UseRedis"/>.
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private Task<string>? _database;
    private string? _redis;

    /// <summary>
    /// Gọi ở <c>InitializeAsync</c> của lớp test, trước CreateClient đầu tiên. xUnit dựng lại lớp test cho từng test
    /// nhưng factory (IClassFixture) sống cả lớp → gọi lại trả đúng database đã tạo, không tạo thêm.
    /// </summary>
    public Task UseFreshDatabaseAsync(PostgresFixture postgres) => _database ??= CreateMigratedDatabaseAsync(postgres);

    /// <summary>D8: Redis thật (Testcontainers). Gọi trước CreateClient đầu tiên.</summary>
    public void UseRedis(string connectionString) => _redis ??= connectionString;

    public string ConnectionString => _database is { IsCompletedSuccessfully: true } db
        ? db.Result
        : throw new InvalidOperationException("Gọi UseFreshDatabaseAsync trước CreateClient.");

    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();

    private static async Task<string> CreateMigratedDatabaseAsync(PostgresFixture postgres)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var services = new ServiceCollection().AddIdentityModule(cs).BuildServiceProvider();
        await services.MigrateIdentityModuleAsync();   // migrate → seed → kiểm tra vai trò hệ thống
        return cs;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", _redis ?? ApiFactory.UnreachableRedis);
        TestJwt.Configure(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
        });
    }
}
