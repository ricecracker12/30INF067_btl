using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.Modules.Identity.DependencyInjection;
using Testcontainers.PostgreSql;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// MỘT container Postgres cho cả collection; mỗi test tự xin một database riêng.
/// Chia container để nhanh, KHÔNG chia database để test không nhìn thấy dữ liệu của nhau.
///
/// Luật chọn hàm: test SỬA dữ liệu → <see cref="CreateDatabaseAsync"/>; test chỉ ĐỌC dữ liệu nền →
/// <see cref="SeededIdentityDatabaseAsync"/>. Chạy chung DB giữa test sửa và test đọc là đỏ ngẫu nhiên
/// theo thứ tự chạy (SEED-02 xóa một dòng role_permissions mà AuthZ matrix đang dựa vào).
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

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }
            .ConnectionString;
    }

    /// <summary>
    /// Database đã migrate + seed, tạo MỘT lần cho mỗi <paramref name="key"/> rồi dùng lại.
    /// Dành cho test chỉ ĐỌC dữ liệu nền (AuthZ matrix) — test nào sửa dữ liệu thì dùng CreateDatabaseAsync.
    /// </summary>
    public Task<string> SeededIdentityDatabaseAsync(string key) =>
        _shared.GetOrAdd(key, _ => new Lazy<Task<string>>(async () =>
        {
            var cs = await CreateDatabaseAsync();
            await using var services = new ServiceCollection().AddIdentityModule(cs).BuildServiceProvider();
            await services.MigrateIdentityModuleAsync();   // migrate → seed → kiểm tra vai trò (A6)
            return cs;
        })).Value;
}
