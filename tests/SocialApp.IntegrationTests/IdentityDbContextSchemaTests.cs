using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Harness Postgres thật (Mục 10.1) — khối B GĐ1 xây tiếp AC-01→AC-04 và AuthZ matrix trên khuôn này.
///
/// Hai việc test này làm:
/// 1. Khoá hành vi tách schema của <see cref="IdentityDbContext"/> (Nợ 2, Mục 9.0). Trước đó chỉ
///    được kiểm bằng tay; giờ có test giữ, ai đổi nhầm là đỏ.
/// 2. Là phép thử sớm cho rủi ro đã đăng ký ở Mục 9.0: CI runner có chạy được Docker daemon không.
///    Đây là lý do test này cần được đẩy lên CI ở PR ĐẦU TIÊN của GĐ1 — GOAL-03 yêu cầu AuthZ
///    matrix làm cổng chặn merge, mà cổng đó chỉ đứng được nếu Testcontainers chạy trên runner.
/// </summary>
public sealed class IdentityDbContextSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_identity()
    {
        var services = new ServiceCollection()
            .AddIdentityModule(_postgres.GetConnectionString())
            .BuildServiceProvider();

        await services.MigrateIdentityModuleAsync();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Bảng lịch sử migration phải nằm trong schema riêng của module, KHÔNG rơi vào "public" —
        // nếu rơi vào public thì GĐ2 thêm context thứ hai sẽ tranh __EFMigrationsHistory.
        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value"
                from information_schema.tables
                where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([IdentityDbContext.Schema], schemas);
    }
}
