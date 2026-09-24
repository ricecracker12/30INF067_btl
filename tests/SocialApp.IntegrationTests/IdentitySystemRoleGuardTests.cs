using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// GĐ6 A3 — migration <c>SystemRoleGuardAndPermissionDescriptions</c> trên Postgres thật:
/// <list type="bullet">
/// <item><c>ROLE-05</c>: trigger <c>trg_roles_protect_system</c> (Đ-6.9 lớp chặn 3) chặn đổi <c>code</c> / xóa ba vai trò hệ
/// thống — kể cả SQL gõ tay — mà vẫn cho đổi <c>display_name</c>.</item>
/// <item>Mô tả quyền đi hai đường (L-A2): DB đã seed TRƯỚC migration (staging) nhận mô tả từ migration.</item>
/// <item>Sequence <c>roles_role_id_seq</c> bắt đầu từ 100 (L-A4).</item>
/// </list>
/// Kỳ vọng viết tay, không đọc <c>PermissionCodes</c>/<c>RoleCodes</c> — cùng luật với <see cref="IdentitySeederTests"/>.
/// Mỗi test một database mới; <see cref="DisposeAsync"/> trả kết nối ngay (xem <see cref="ModerationDbContextSchemaTests"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdentitySystemRoleGuardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection().AddIdentityModule(_connectionString).BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_connectionString);
        NpgsqlConnection.ClearPool(conn);
    }

    private async Task<int> ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> QueryAsync(FormattableString sql)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.SqlQuery<string>(sql).ToListAsync();
    }

    /// <summary>ROLE-05: đổi <c>code</c> hay xóa một vai trò hệ thống bằng SQL thẳng → <c>P0001</c>; dữ liệu không đổi.</summary>
    [Theory]
    [InlineData("update identity.roles set code = 'USERS' where code = 'USER'")]
    [InlineData("update identity.roles set code = 'ROOT' where code = 'ADMIN'")]
    [InlineData("delete from identity.roles where code = 'MODERATOR'")]
    public async Task ROLE_05_doi_code_hoac_xoa_vai_tro_he_thong_bi_DB_tu_choi(string sql)
    {
        await _services.MigrateIdentityModuleAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.RaiseException, ex.SqlState);   // P0001 — RAISE của trigger
        Assert.Equal(
            ["1|USER", "2|MODERATOR", "3|ADMIN"],
            await QueryAsync($"""select role_id || '|' || code as "Value" from identity.roles order by role_id"""));
    }

    /// <summary>
    /// ROLE-05, vế được phép: đổi <c>display_name</c> của vai trò hệ thống (D5 <c>PATCH /admin/roles</c>) và <c>UPDATE</c> giữ
    /// nguyên <c>code</c> không bị chặn oan — trigger là <c>UPDATE OF code</c> + so <c>IS DISTINCT FROM</c>.
    /// </summary>
    [Fact]
    public async Task ROLE_05_doi_display_name_va_update_giu_nguyen_code_thi_duoc()
    {
        await _services.MigrateIdentityModuleAsync();

        Assert.Equal(1, await ExecuteAsync("update identity.roles set display_name = 'Thành viên' where code = 'USER'"));
        Assert.Equal(1, await ExecuteAsync("update identity.roles set code = code where code = 'ADMIN'"));

        Assert.Contains("1|USER|Thành viên",
            await QueryAsync($"""select role_id || '|' || code || '|' || display_name as "Value" from identity.roles"""));
    }

    /// <summary>Vai trò TỰ TẠO đổi code / xóa tự do — trigger chỉ canh ba mã hệ thống (<c>WHEN OLD.code IN …</c>).</summary>
    [Fact]
    public async Task ROLE_05_vai_tro_tu_tao_doi_code_va_xoa_duoc()
    {
        await _services.MigrateIdentityModuleAsync();
        await ExecuteAsync("insert into identity.roles (role_id, code, display_name) values (100, 'REVIEWER', 'Người duyệt')");

        Assert.Equal(1, await ExecuteAsync("update identity.roles set code = 'REVIEWER2' where role_id = 100"));
        Assert.Equal(1, await ExecuteAsync("delete from identity.roles where role_id = 100"));
    }

    /// <summary>
    /// L-A2: DB đã seed bởi seeder GĐ1 (17 quyền, mô tả NULL) rồi mới chạy migration A3 — đúng hình dạng staging. Migration điền
    /// 17 mô tả; seeder chạy sau chèn dòng 18 kèm mô tả. Không dòng nào NULL.
    /// </summary>
    [Fact]
    public async Task Mo_ta_quyen_duoc_dien_cho_DB_da_seed_tu_GD1()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20260913040158_InitialIdentity");
        }

        // Seeder GĐ1 nguyên dạng: 17 dòng, không cột description.
        await ExecuteAsync("""
            insert into identity.permissions (permission_id, code) values
              (1,'post.read.public'),(2,'post.read.friends'),(3,'post.create'),(4,'post.update'),(5,'post.delete'),
              (6,'post.hide'),(7,'comment.create'),(8,'reaction.set'),(9,'friend.request'),(10,'friend.respond'),
              (11,'message.send'),(12,'report.create'),(13,'report.resolve'),(14,'user.lock'),(15,'user.unlock'),
              (16,'role.assign'),(17,'audit.read')
            """);

        await _services.MigrateIdentityModuleAsync();   // migration A3 → seeder

        var descriptions = await QueryAsync(
            $"""select permission_id || '|' || coalesce(description, '') as "Value" from identity.permissions order by permission_id""");

        Assert.Equal(18, descriptions.Count);
        Assert.DoesNotContain(descriptions, d => d.EndsWith('|'));
        Assert.Equal("1|Xem bài viết công khai", descriptions[0]);                          // từ migration
        Assert.Equal("18|Tạo, đổi tên, sửa quyền và xóa vai trò", descriptions[17]);        // từ seeder
    }

    /// <summary>Mô tả ai đó đã sửa tay trên staging không bị migration đè (<c>AND description IS NULL</c>).</summary>
    [Fact]
    public async Task Mo_ta_da_co_khong_bi_migration_de()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20260913040158_InitialIdentity");
        }

        await ExecuteAsync("insert into identity.permissions (permission_id, code, description) values (6, 'post.hide', 'Ẩn bài')");

        await _services.MigrateIdentityModuleAsync();

        Assert.Contains("6|Ẩn bài",
            await QueryAsync($"""select permission_id || '|' || description as "Value" from identity.permissions"""));
    }

    /// <summary>L-A4: id đầu tiên cho vai trò tự tạo là 100 — không bao giờ đụng 1/2/3 của vai trò hệ thống.</summary>
    [Fact]
    public async Task Sequence_vai_tro_tu_tao_bat_dau_tu_100()
    {
        await _services.MigrateIdentityModuleAsync();

        var first = await QueryAsync($"""select nextval('identity.roles_role_id_seq')::text as "Value" """);

        Assert.Equal(["100"], first);
    }
}
