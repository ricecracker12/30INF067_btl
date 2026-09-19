using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Profile.DependencyInjection;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// D0 (GĐ2): dựng app cho MỌI test endpoint của khối D — Profile (D1–D3) và Content (D4–D8). Một factory cho cả hai module
/// vì chúng chạy trong cùng một host và test của D5 cần cả hai (đăng bài đòi có hồ sơ — Đ-2.4).
///
/// Theo khuôn <see cref="Auth.IdentityApiFactory"/> của GĐ1, khác ở bốn chỗ:
/// <list type="number">
/// <item>Database riêng mỗi lớp test, migrate CẢ BA module: test khối D SỬA dữ liệu (luật B1), và tầng 2 đọc
/// <c>identity.role_permissions</c> nên Identity phải được seed dù không endpoint nào của khối D thuộc Identity.</item>
/// <item><see cref="Storage"/> là <see cref="FakeObjectStorage"/> — chép nguyên cách <see cref="AuthZ.AuthZApiFactory"/>
/// thay <see cref="IObjectStorage"/> (C5). CI không có khóa R2 và sẽ không bao giờ có.</item>
/// <item>KHÔNG có <c>CapturingEmailSender</c>: khối D không gửi mail nào. Thêm vào "cho đối xứng" là một phụ thuộc không
/// ai đọc.</item>
/// <item>Token bằng <c>TestJwt.Create("USER", userId: id)</c> — KHÔNG đăng ký + đăng nhập thật như GĐ1. Profile/Content
/// không có FK sang <c>identity.users</c> (Đ-2.2), tầng 1 chỉ cần chữ ký hợp lệ và tầng 2 chỉ cần claim <c>role</c>. Đây là
/// điểm khác GĐ1 có chủ đích: ở đó <c>/me</c> đọc bảng <c>users</c> nên <c>sub</c> phải là người có thật.</item>
/// </list>
/// Redis không tới được như <see cref="ApiFactory"/> — bên đọc thu hồi token fail-open nên tầng 1 vẫn chạy.
/// </summary>
public sealed class ModulesApiFactory : WebApplicationFactory<Program>
{
    private Task<string>? _database;

    /// <summary>
    /// C5: lưu trữ đối tượng giả. Test dựng sẵn object bằng <c>Storage.Put(...)</c> rồi gọi API thật. MỘT instance cho cả
    /// factory, nên hai test trong cùng lớp thấy chung dữ liệu — dùng key có Guid riêng, đừng dùng key cố định.
    /// </summary>
    public FakeObjectStorage Storage { get; } = new();

    /// <summary>
    /// Gọi ở <c>InitializeAsync</c> của lớp test, trước CreateClient đầu tiên. xUnit dựng lại lớp test cho từng test nhưng
    /// factory (IClassFixture) sống cả lớp → gọi lại trả đúng database đã tạo, không tạo thêm.
    /// </summary>
    public Task UseFreshDatabaseAsync(PostgresFixture postgres) => _database ??= CreateMigratedDatabaseAsync(postgres);

    public string ConnectionString => _database is { IsCompletedSuccessfully: true } db
        ? db.Result
        : throw new InvalidOperationException("Gọi UseFreshDatabaseAsync trước CreateClient.");

    /// <summary>
    /// Thứ tự Identity → Profile → Content CỐ Ý ghi ra dù không có FK chéo schema (Đ-2.2) — cùng thứ tự với
    /// <c>PostgresFixture.SeededContentDatabaseAsync</c> và với hook <c>--migrate</c> của Program.cs, để log đọc được theo
    /// một thứ tự không đổi. Seeder vai trò/quyền nằm trong <c>MigrateIdentityModuleAsync</c>: quên dòng đó là mọi test có
    /// <c>[RequirePermission]</c> đỏ với triệu chứng trông hệt "handler hỏng".
    /// </summary>
    private static async Task<string> CreateMigratedDatabaseAsync(PostgresFixture postgres)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddIdentityModule(cs)
            .AddProfileModule(cs)
            .AddContentModule(cs)
            .BuildServiceProvider();

        await services.MigrateIdentityModuleAsync();   // migrate → seed vai trò/quyền → kiểm tra vai trò hệ thống
        await services.MigrateProfileModuleAsync();
        await services.MigrateContentModuleAsync();
        return cs;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
        TestJwt.Configure(builder);

        builder.ConfigureTestServices(services =>
        {
            // Đ-D7: IP giả cho từng request. Quên dòng này là test thứ 101 của một lớp nhận 429, trông như flaky.
            services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();

            // C5. RemoveAll TRƯỚC: hai đăng ký thì DI lấy cái cuối theo thứ tự gọi, tức là đỏ ngẫu nhiên. Program.cs đăng ký
            // bằng TryAdd nên đây là chỗ DUY NHẤT quyết định hiện thực IObjectStorage trong test.
            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<IObjectStorage>(Storage);
        });
    }
}
