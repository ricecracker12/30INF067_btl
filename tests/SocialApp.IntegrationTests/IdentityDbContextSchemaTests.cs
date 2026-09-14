using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Test schema trên Postgres thật (Mục 10.1), chạy trên harness dùng chung <see cref="PostgresFixture"/> —
/// AC-01→AC-04 và AuthZ matrix cũng dựng trên fixture đó. Mỗi test một database mới, chưa migrate.
///
/// Hai việc test này làm:
/// 1. Khoá hành vi tách schema của <see cref="IdentityDbContext"/> (Nợ 2, Mục 9.0). Trước đó chỉ
///    được kiểm bằng tay; giờ có test giữ, ai đổi nhầm là đỏ.
/// 2. Là phép thử sớm cho rủi ro đã đăng ký ở Mục 9.0: CI runner có chạy được Docker daemon không.
///    Đây là lý do test này cần được đẩy lên CI ở PR ĐẦU TIÊN của GĐ1 — GOAL-03 yêu cầu AuthZ
///    matrix làm cổng chặn merge, mà cổng đó chỉ đứng được nếu Testcontainers chạy trên runner.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdentityDbContextSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_identity()
    {
        var services = new ServiceCollection()
            .AddIdentityModule(await postgres.CreateDatabaseAsync())
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

    /// <summary>
    /// Khóa những bất biến mà Mục 4 giao cho DB giữ (A2/A3). Mỗi khẳng định ứng với một thứ mà nếu
    /// cấu hình EF sai thì KHÔNG có lỗi nào khác báo — migration vẫn chạy, app vẫn lên.
    /// </summary>
    [Fact]
    public async Task Migrate_tao_du_bang_va_rang_buoc_cua_Muc_4()
    {
        var services = new ServiceCollection()
            .AddIdentityModule(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();

        await services.MigrateIdentityModuleAsync();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // 1. Đủ 6 bảng nghiệp vụ, tất cả trong schema identity.
        var tables = await db.Database
            .SqlQuery<string>($"""
                select table_name as "Value"
                from information_schema.tables
                where table_schema = 'identity' and table_name <> '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal(
            ["email_verification_tokens", "permissions", "refresh_tokens", "role_permissions", "roles", "users"],
            tables.Order());

        // 2. FK users.role_id là RESTRICT — biện pháp #1 thay cho is_system (Mục 3.4). Mặc định của EF
        //    cho FK required là CASCADE, tức xóa vai trò sẽ xóa sạch người dùng.
        var usersDeleteRules = await db.Database
            .SqlQuery<string>($"""
                select rc.delete_rule as "Value"
                from information_schema.referential_constraints rc
                join information_schema.table_constraints tc
                  on tc.constraint_name = rc.constraint_name and tc.constraint_schema = rc.constraint_schema
                where tc.table_schema = 'identity' and tc.table_name = 'users'
                """)
            .ToListAsync();

        Assert.Equal(["RESTRICT"], usersDeleteRules);

        // 3. Extension citext đã bật — users.email dựa vào nó.
        var extensions = await db.Database
            .SqlQuery<string>($"select extname as \"Value\" from pg_extension where extname = 'citext'")
            .ToListAsync();

        Assert.Equal(["citext"], extensions);

        // 4. DEFAULT now() có mặt trên mọi cột created_at/updated_at (Mục 4 "Nguồn thời gian"). App luôn
        //    gửi giá trị tường minh nên thiếu default không làm hỏng đường EF — chỉ làm hỏng INSERT bằng
        //    SQL thô của seeder, và chỉ lộ ra lúc đó.
        var timestampDefaults = await db.Database
            .SqlQuery<string>($"""
                select table_name || '.' || column_name || '=' || coalesce(column_default, '<none>') as "Value"
                from information_schema.columns
                where table_schema = 'identity' and column_name in ('created_at', 'updated_at')
                """)
            .ToListAsync();

        Assert.Equal(
            [
                "refresh_tokens.created_at=now()",
                "roles.created_at=now()",
                "roles.updated_at=now()",
                "users.created_at=now()",
                "users.updated_at=now()",
            ],
            timestampDefaults.Order());
    }
}
