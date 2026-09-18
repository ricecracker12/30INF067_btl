using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Profile.DependencyInjection;
using SocialApp.Modules.Profile.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Bản Profile của <see cref="IdentityDbContextSchemaTests"/> (A3, GĐ2 khối A): schema trên Postgres
/// thật, mỗi test một database mới chưa migrate, trên cùng <see cref="PostgresFixture"/>.
///
/// Ba khẳng định ở đây đều ứng với một cách hỏng mà KHÔNG có lỗi nào khác báo — migration vẫn chạy,
/// app vẫn lên, chỉ sai ở chỗ không ai nhìn.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProfileDbContextSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_profile()
    {
        var services = new ServiceCollection()
            .AddProfileModule(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();

        await services.MigrateProfileModuleAsync();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();

        // Bảng lịch sử phải nằm trong schema riêng của module, KHÔNG rơi vào "public": ba context của
        // GĐ1–GĐ2 mà tranh một __EFMigrationsHistory thì migration của module này "biến mất" với module kia.
        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value"
                from information_schema.tables
                where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([ProfileDbContext.Schema], schemas);
    }

    [Fact]
    public async Task Migrate_tao_bang_profiles_dung_schema_va_du_sau_cot()
    {
        var services = new ServiceCollection()
            .AddProfileModule(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();

        await services.MigrateProfileModuleAsync();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();

        // 1. Đúng một bảng nghiệp vụ, nằm trong schema profile.
        var tables = await db.Database
            .SqlQuery<string>($"""
                select table_name as "Value"
                from information_schema.tables
                where table_schema = 'profile' and table_name <> '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal(["profiles"], tables.Order());

        // 2. Đủ 6 cột, tên snake_case. Cột PascalCase thì mọi câu SQL viết tay và mọi bản dump sau này
        //    đều lệch — mà EF thì vẫn chạy bình thường nên không có gì báo.
        var columns = await db.Database
            .SqlQuery<string>($"""
                select column_name as "Value"
                from information_schema.columns
                where table_schema = 'profile' and table_name = 'profiles'
                """)
            .ToListAsync();

        Assert.Equal(
            ["avatar_key", "bio", "created_at", "display_name", "updated_at", "user_id"],
            columns.Order());
    }

    /// <summary>
    /// CHECK <c>ck_profiles_display_name_not_blank</c> phải chặn THẬT, không chỉ "có mặt trong migration".
    /// Đi bằng SQL thô chứ không qua EF: đây là lưới cuối cho đường không có validator của D2 đứng trước.
    /// </summary>
    [Fact]
    public async Task Check_constraint_chan_display_name_toan_khoang_trang()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection()
            .AddProfileModule(connectionString)
            .BuildServiceProvider();

        await services.MigrateProfileModuleAsync();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            insert into profile.profiles (user_id, display_name, created_at, updated_at)
            values (gen_random_uuid(), '   ', now(), now())
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_profiles_display_name_not_blank", ex.ConstraintName);
    }
}
