using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Messaging.DependencyInjection;
using SocialApp.Modules.Moderation.DependencyInjection;
using SocialApp.Modules.Notification.DependencyInjection;
using SocialApp.Modules.Profile.DependencyInjection;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// D0 (GĐ2): dựng app cho MỌI test endpoint của khối D — Profile (D1–D3) và Content (D4–D8). Một factory cho cả hai module
/// vì chúng chạy trong cùng một host và test của D5 cần cả hai (đăng bài đòi có hồ sơ — Đ-2.4).
///
/// Theo khuôn <see cref="Auth.IdentityApiFactory"/> của GĐ1, khác ở bốn chỗ:
/// <list type="number">
/// <item>Database riêng mỗi lớp test, migrate CẢ BỐN module: test khối D SỬA dữ liệu (luật B1), và tầng 2 đọc
/// <c>identity.role_permissions</c> nên Identity phải được seed dù không endpoint nào của khối D thuộc Identity.</item>
/// <item><see cref="Storage"/> là <see cref="FakeObjectStorage"/> — chép nguyên cách <see cref="AuthZ.AuthZApiFactory"/>
/// thay <see cref="IObjectStorage"/> (C5). CI không có khóa R2 và sẽ không bao giờ có.</item>
/// <item>KHÔNG có <c>CapturingEmailSender</c>: khối D không gửi mail nào. Thêm vào "cho đối xứng" là một phụ thuộc không
/// ai đọc.</item>
/// <item>Token bằng <c>TestJwt.Create("USER", userId: id)</c> — KHÔNG đăng ký + đăng nhập thật như GĐ1. Profile/Content
/// không có FK sang <c>identity.users</c> (Đ-2.2), tầng 1 chỉ cần chữ ký hợp lệ và tầng 2 chỉ cần claim <c>role</c>. Đây là
/// điểm khác GĐ1 có chủ đích: ở đó <c>/me</c> đọc bảng <c>users</c> nên <c>sub</c> phải là người có thật.</item>
/// </list>
/// Redis mặc định không tới được như <see cref="ApiFactory"/> — bên đọc thu hồi token fail-open nên tầng 1 vẫn chạy.
/// B1 (GĐ4): test cache feed gọi <see cref="UseRedis"/> với Redis thật; không gọi thì Redis vẫn cổng 1.
/// </summary>
public sealed class ModulesApiFactory : WebApplicationFactory<Program>
{
    private Task<string>? _database;
    private string _redis = ApiFactory.UnreachableRedis;   // mặc định GIỮ NGUYÊN: mọi lớp cũ vẫn chạy không Redis
    private Action<IServiceCollection>? _testServices;
    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);

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

    /// <summary>
    /// GĐ6 C3 (PERM-02): dùng database ĐÃ migrate của một factory khác — hai host chung DB + chung Redis mô phỏng hai bản sao API.
    /// Gọi thay cho <see cref="UseFreshDatabaseAsync"/>, trước CreateClient đầu tiên.
    /// </summary>
    public void UseDatabase(string connectionString) => _database ??= Task.FromResult(connectionString);

    /// <summary>
    /// B1 (GĐ4): Redis THẬT cho test cache feed. Gọi ở InitializeAsync, trước CreateClient đầu tiên — cùng luật với
    /// UseFreshDatabaseAsync. Không gọi thì Redis là cổng 1: cache fail-open, và test cache xanh vì lý do sai.
    /// </summary>
    public void UseRedis(string connectionString) => _redis = connectionString;

    /// <summary>
    /// C0 (GĐ6): thêm dịch vụ riêng của MỘT lớp test — vd handler event ghi lại (<c>SocialGraphEventsTests</c>). Gọi ở
    /// InitializeAsync, trước CreateClient đầu tiên — cùng luật với <see cref="UseRedis"/>. An toàn vì
    /// <c>IClassFixture&lt;ModulesApiFactory&gt;</c> dựng một factory cho mỗi lớp test, không dùng chung giữa các lớp.
    /// </summary>
    public void UseTestServices(Action<IServiceCollection> configure) => _testServices = configure;

    /// <summary>
    /// C4 (GĐ5): đặt một khóa cấu hình của host (vd <c>Realtime:Backplane:Enabled</c>). Gọi trước CreateClient đầu tiên — cùng luật
    /// với <see cref="UseRedis"/>.
    /// </summary>
    public void UseSetting(string key, string value) => _settings[key] = value;

    public string ConnectionString => _database is { IsCompletedSuccessfully: true } db
        ? db.Result
        : throw new InvalidOperationException("Gọi UseFreshDatabaseAsync trước CreateClient.");

    /// <summary>
    /// Thứ tự Identity → Profile → Content → SocialGraph → Messaging → Moderation → Notification CỐ Ý ghi ra dù không có FK chéo schema
    /// (Đ-2.2) — cùng thứ tự với <c>PostgresFixture.SeededContentDatabaseAsync</c> và với hook <c>--migrate</c> của Program.cs,
    /// để log đọc được theo một thứ tự không đổi. Seeder vai trò/quyền nằm trong <c>MigrateIdentityModuleAsync</c>: quên dòng đó là mọi test có
    /// <c>[RequirePermission]</c> đỏ với triệu chứng trông hệt "handler hỏng".
    /// </summary>
    private static async Task<string> CreateMigratedDatabaseAsync(PostgresFixture postgres)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddIdentityModule(cs)
            .AddProfileModule(cs)
            .AddContentModule(cs)
            .AddSocialGraphModule(cs)
            .AddMessagingModule(cs)
            .AddModerationModule(cs)
            .AddNotificationModule(cs)
            .BuildServiceProvider();

        await services.MigrateIdentityModuleAsync();   // migrate → seed vai trò/quyền → kiểm tra vai trò hệ thống
        await services.MigrateProfileModuleAsync();
        await services.MigrateContentModuleAsync();
        await services.MigrateSocialGraphModuleAsync();
        await services.MigrateMessagingModuleAsync();
        await services.MigrateModerationModuleAsync();
        await services.MigrateNotificationModuleAsync();
        return cs;
    }

    /// <summary>
    /// Trả pool kết nối của database này về Postgres khi lớp test xong (GĐ5, 2026-09-24). Mỗi lớp dùng factory có database riêng,
    /// và pool Npgsql giữ kết nối rảnh tới 300 giây — trong khi cả collection chung MỘT container <c>max_connections = 100</c>.
    /// Thêm ba lớp test hub của GĐ5 là đủ đẩy các lớp chạy sau sang <c>53300 too many clients already</c> (đo: 5 ca AuthZ đỏ).
    /// Cùng lý do với <c>ClearPool</c> của <c>ModerationDbContextSchemaTests</c>, nhưng ở MỘT chỗ cho mọi lớp dùng factory.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is { IsCompletedSuccessfully: true } database)
        {
            await using var conn = new NpgsqlConnection(database.Result);
            NpgsqlConnection.ClearPool(conn);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", _redis);
        TestJwt.Configure(builder);
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            // Đ-D7: IP giả cho từng request. Quên dòng này là test thứ 101 của một lớp nhận 429, trông như flaky.
            services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();

            // C5. RemoveAll TRƯỚC: hai đăng ký thì DI lấy cái cuối theo thứ tự gọi, tức là đỏ ngẫu nhiên. Program.cs đăng ký
            // bằng TryAdd nên đây là chỗ DUY NHẤT quyết định hiện thực IObjectStorage trong test.
            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<IObjectStorage>(Storage);

            _testServices?.Invoke(services);
        });
    }
}
