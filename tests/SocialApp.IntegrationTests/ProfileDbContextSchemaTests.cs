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
/// Các khẳng định ở đây đều ứng với một cách hỏng mà KHÔNG có lỗi nào khác báo — migration vẫn chạy,
/// app vẫn lên, chỉ sai ở chỗ không ai nhìn.
///
/// Ba ca tìm kiếm không dấu (A4 GĐ6, Đ-6.19) đi qua <see cref="MigratedAsync"/> để <see cref="DisposeAsync"/> trả kết nối ngay
/// (<c>ClearPool</c>) — lý do ở <see cref="ModerationDbContextSchemaTests"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProfileDbContextSchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ServiceProvider? _services;
    private string? _connectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        if (_connectionString is not null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(conn);
        }
    }

    private async Task<NpgsqlConnection> MigratedAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection()
            .AddProfileModule(_connectionString)
            .BuildServiceProvider();
        await _services.MigrateProfileModuleAsync();

        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

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

    /// <summary>
    /// A4 (Đ-6.19): <c>profile.search_norm</c> tồn tại và là <c>IMMUTABLE</c> (<c>provolatile = 'i'</c>) — hàm <c>STABLE</c> thì
    /// Postgres từ chối index biểu thức, và đó đúng là bẫy của <c>unaccent</c> trần. Hai extension có mặt.
    /// </summary>
    [Fact]
    public async Task Search_norm_la_immutable_va_hai_extension_co_mat()
    {
        await using var conn = await MigratedAsync();

        await using (var cmd = new NpgsqlCommand("""
            select p.provolatile::text from pg_proc p join pg_namespace n on n.oid = p.pronamespace
            where n.nspname = 'profile' and p.proname = 'search_norm'
            """, conn))
        {
            Assert.Equal("i", (string?)await cmd.ExecuteScalarAsync());
        }

        await using (var cmd = new NpgsqlCommand(
            "select string_agg(extname, ',' order by extname) from pg_extension where extname in ('pg_trgm', 'unaccent')", conn))
        {
            Assert.Equal("pg_trgm,unaccent", (string?)await cmd.ExecuteScalarAsync());
        }
    }

    /// <summary>
    /// <c>SRCH-03</c> ở tầng hàm: bỏ dấu, hạ chữ thường, và "đ/Đ" → "d" — <c>unaccent</c> bản chuẩn làm vậy, nhưng Đ-6.19 bảo kiểm
    /// bằng test chứ không tin. Tên không dấu sẵn đi qua nguyên vẹn (chỉ hạ chữ thường).
    /// </summary>
    [Theory]
    [InlineData("Nguyễn Đức Ánh", "nguyen duc anh")]
    [InlineData("ĐẶNG THỊ HẠNH", "dang thi hanh")]
    [InlineData("Perf User 7", "perf user 7")]
    public async Task Search_norm_bo_dau_ha_chu_thuong_va_doi_d(string input, string expected)
    {
        await using var conn = await MigratedAsync();

        await using var cmd = new NpgsqlCommand("select profile.search_norm(@s)", conn);
        cmd.Parameters.AddWithValue("s", input);

        Assert.Equal(expected, (string?)await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Biểu thức truy vấn KHỚP index <c>idx_profiles_display_name_search</c>: với Seq Scan bị tắt, <c>EXPLAIN</c> của hai vế
    /// tiền tố (Đ-6.19) dùng đúng index đó.
    ///
    /// Đây KHÔNG phải <c>SRCH-07</c>: bảng rỗng + <c>enable_seqscan = off</c> chỉ chứng minh biểu thức và operator class khớp
    /// index — không chứng minh planner CHỌN index ở quy mô thật. Bằng chứng đó là <c>EXPLAIN (ANALYZE)</c> trên 20.000 hồ sơ
    /// (<c>tests/load/search/seed-profiles.sql</c>, kết quả ở "Thực tế thi công" A4 của hướng dẫn khối A+C).
    /// </summary>
    [Fact]
    public async Task Bieu_thuc_tien_to_khop_index_gin()
    {
        await using var conn = await MigratedAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var off = new NpgsqlCommand("set local enable_seqscan = off", conn, tx))
            await off.ExecuteNonQueryAsync();

        await using var cmd = new NpgsqlCommand("""
            explain select user_id from profile.profiles
            where profile.search_norm(display_name) like 'ng%' or profile.search_norm(display_name) like '% ng%'
            """, conn, tx);
        var plan = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));

        Assert.Contains(plan, line => line.Contains("idx_profiles_display_name_search", StringComparison.Ordinal));
    }
}
