using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.SharedKernel.Configuration;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Moderation.DependencyInjection;
using SocialApp.Modules.Profile.DependencyInjection;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using Testcontainers.PostgreSql;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// MỘT container Postgres cho cả collection; mỗi test tự xin một database riêng.
/// Chia container để nhanh, KHÔNG chia database để test không nhìn thấy dữ liệu của nhau.
///
/// Luật chọn hàm: test SỬA dữ liệu → <see cref="CreateDatabaseAsync"/>; test chỉ ĐỌC dữ liệu nền →
/// <see cref="SeededContentDatabaseAsync"/> (mọi module có schema, từ GĐ4) hoặc <see cref="SeededIdentityDatabaseAsync"/>
/// (chỉ Identity). Chạy chung DB giữa test sửa và test đọc là đỏ ngẫu nhiên theo thứ tự chạy (SEED-02 xóa một
/// dòng role_permissions mà AuthZ matrix đang dựa vào).
///
/// Cache database dùng chung khóa theo <c>"&lt;hàm&gt;:&lt;key&gt;"</c>, KHÔNG theo <c>key</c> trần: hai hàm cùng
/// <c>"authz"</c> mà dùng chung một ô cache thì database thật là của hàm nào chạy TRƯỚC — khác nhau giữa các lần
/// chạy tùy thứ tự xUnit, và là loại đỏ ngẫu nhiên tốn cả buổi (B1 GĐ2, Q-B1). Khóa theo hàm làm nó không thể xảy
/// ra; cái giá (thêm một lượt migrate) chỉ trả khi ai đó thật sự trộn hai hàm với cùng key.
///
/// Test cùng collection chạy TUẦN TỰ — cái giá của chia container. Khi nhóm này vượt ~3 phút thì tách
/// thành 2–3 collection, mỗi collection một container; đừng quay về mỗi test một container.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _shared = new();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Database mới, rỗng hoàn toàn — chưa migrate. Dùng cho test cần kiểm lần đầu tiên.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            // KHÔNG bọc trong transaction: Postgres từ chối CREATE DATABASE bên trong transaction.
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // Trần pool ghi SẴN trong chuỗi (PERF-03): app chỉ thêm "Maximum Pool Size" khi chuỗi chưa có, và Npgsql khóa pool
        // theo NGUYÊN VĂN chuỗi. Không ghi sẵn thì app (chuỗi đã thêm trần) và helper của test (chuỗi gốc) mở HAI pool cho mỗi
        // database; kết nối rỗi gấp đôi và cả bộ test chạm max_connections 100 của container ("too many clients already").
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            MaxPoolSize = PostgresPool.DefaultMaxPoolSize,
        }.ConnectionString;
    }

    /// <summary>
    /// Database đã migrate + seed CHỈ module Identity, tạo MỘT lần cho mỗi <paramref name="key"/> rồi dùng lại.
    /// Dành cho test chỉ ĐỌC dữ liệu nền — test nào sửa dữ liệu thì dùng CreateDatabaseAsync.
    /// Từ GĐ2 các test đọc dùng chung "authz" đã chuyển sang <see cref="SeededContentDatabaseAsync"/>; hàm này giữ
    /// lại cho test nào thật sự chỉ cần Identity.
    /// </summary>
    public Task<string> SeededIdentityDatabaseAsync(string key) =>
        _shared.GetOrAdd("identity:" + key, _ => new Lazy<Task<string>>(async () =>
        {
            var cs = await CreateDatabaseAsync();
            await using var services = new ServiceCollection().AddIdentityModule(cs).BuildServiceProvider();
            await services.MigrateIdentityModuleAsync();   // migrate → seed → kiểm tra vai trò (A6)
            return cs;
        })).Value;

    /// <summary>
    /// Database đã migrate mọi module có schema + seed Identity, tạo MỘT lần cho mỗi <paramref name="key"/> rồi dùng lại.
    /// Dành cho test chỉ ĐỌC dữ liệu nền và cần bảng của Profile/Content (AuthZ matrix từ GĐ2: TC-A03 gọi
    /// /api/v1/posts). Test nào SỬA dữ liệu nền thì vẫn dùng CreateDatabaseAsync — luật chọn hàm của GĐ1 không đổi.
    ///
    /// Thứ tự Identity → Profile → Content → SocialGraph → Moderation là CỐ Ý ghi ra dù không có phụ thuộc nào giữa chúng
    /// (Đ-2.2: không FK qua ranh giới schema) — cùng thứ tự hook <c>--migrate</c> của Program.cs. Ghi ra để người đọc sau
    /// không tưởng thứ tự là ngẫu nhiên rồi đảo nó khi thêm module.
    /// SocialGraph vào harness ở A3 (lệch L3) — trước A5, vì thiếu schema thì READ_02_05 nhận 500 khi BR-02 thật chạy.
    /// Moderation vào harness ở A1 của GĐ6 (L-A7) — C4 ghi <c>access.denied</c> vào bảng audit trên chính các DB này.
    /// Seeder vai trò/quyền nằm trong MigrateIdentityModuleAsync, không nằm trong AddIdentityModule —
    /// quên dòng migrate của Identity là RBAC-02b đỏ với triệu chứng trông hệt "handler hỏng".
    /// </summary>
    public Task<string> SeededContentDatabaseAsync(string key) =>
        _shared.GetOrAdd("content:" + key, _ => new Lazy<Task<string>>(async () =>
        {
            var cs = await CreateDatabaseAsync();
            await using var services = new ServiceCollection()
                .AddIdentityModule(cs)
                .AddProfileModule(cs)
                .AddContentModule(cs)
                .AddSocialGraphModule(cs)
                .AddModerationModule(cs)
                .BuildServiceProvider();

            await services.MigrateIdentityModuleAsync();   // migrate → seed vai trò/quyền
            await services.MigrateProfileModuleAsync();
            await services.MigrateContentModuleAsync();
            await services.MigrateSocialGraphModuleAsync();
            await services.MigrateModerationModuleAsync();
            return cs;
        })).Value;
}
