using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;
using SocialApp.Modules.Identity.Infrastructure.Seed;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// SEED-01, SEED-02 (Mục 10.1) — nghiệm thu A4 trên Postgres thật.
///
/// Kỳ vọng viết tay theo Mục 5.1–5.3, CỐ Ý không đọc lại hằng số của seeder hay RoleCodes/PermissionCodes:
/// test dùng chung nguồn với code thì seeder sai kiểu gì test cũng sai theo và vẫn xanh.
///
/// Thuộc khối B5 nhưng viết ở A4, vì A4 chỉ "xong" khi SEED-01/02 xanh. Chạy trên harness dùng chung
/// <see cref="PostgresFixture"/> (B1): chung container, nhưng mỗi test một database MỚI vì test ở đây sửa
/// dữ liệu nền.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdentitySeederTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Mục 5.1 — so cả display_name để bắt lỗi encoding tiếng Việt, không chỉ đếm dòng.
    private static readonly string[] ExpectedRoles =
    [
        "1|USER|Người dùng",
        "2|MODERATOR|Kiểm duyệt viên",
        "3|ADMIN|Quản trị viên",
    ];

    // Mục 5.2 (1..17) + GĐ6 Đ-6.9 (18 role.manage).
    private static readonly string[] ExpectedPermissions =
    [
        "1|post.read.public", "2|post.read.friends", "3|post.create", "4|post.update", "5|post.delete",
        "6|post.hide", "7|comment.create", "8|reaction.set", "9|friend.request", "10|friend.respond",
        "11|message.send", "12|report.create", "13|report.resolve", "14|user.lock", "15|user.unlock",
        "16|role.assign", "17|audit.read", "18|role.manage",
    ];

    // Mục 5.3 — USER 11 dòng, MODERATOR = USER + 6 (post.hide) + 13 (report.resolve), ADMIN không dòng nào.
    // role.manage (18) KHÔNG có ở đây: chỉ ADMIN, qua short-circuit (Đ-6.9) — nên vẫn 24 dòng như GĐ1.
    private static readonly string[] ExpectedGrants =
    [
        .. new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12 }.Select(p => $"1|{p}"),
        .. new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }.Select(p => $"2|{p}"),
    ];

    private ServiceProvider _services = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection()
            .AddIdentityModule(_connectionString)
            .BuildServiceProvider();

        // Chỉ migrate, KHÔNG gọi MigrateIdentityModuleAsync: từ A6 hook đó tự seed, còn SEED-01 phải kiểm
        // lần seed đầu tiên trên DB rỗng chứ không phải lần thứ hai.
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    /// <summary>
    /// Trả kết nối của database riêng này về Postgres ngay (GĐ6 A1): pool Npgsql giữ kết nối rảnh 300 giây mà cả collection chung
    /// một container <c>max_connections = 100</c> — xem <see cref="ModerationDbContextSchemaTests"/>.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_connectionString);
        NpgsqlConnection.ClearPool(conn);
    }

    [Fact]
    public async Task SEED_01_chay_seeder_hai_lan_du_lieu_khong_doi_khong_nhan_ban()
    {
        await SeedAsync();
        var (roles1, permissions1, grants1) = await ReadSeedDataAsync();

        await SeedAsync();
        var (roles2, permissions2, grants2) = await ReadSeedDataAsync();

        Assert.Equal(ExpectedRoles, roles1);
        Assert.Equal(ExpectedPermissions, permissions1);
        Assert.Equal(ExpectedGrants, grants1);
        Assert.Equal(24, grants1.Count);
        Assert.DoesNotContain(grants1, g => g.StartsWith("3|", StringComparison.Ordinal)); // ADMIN: thiết kế 3.2

        Assert.Equal(roles1, roles2);
        Assert.Equal(permissions1, permissions2);
        Assert.Equal(grants1, grants2);

        // GĐ6 L-A2: DB MỚI nhận mô tả từ seeder (migration chạy trước seeder nên không điền được) — không dòng nào NULL. So
        // nguyên văn một dòng để bắt lỗi encoding tiếng Việt, cùng lý do so display_name ở trên.
        var descriptions = await ReadDescriptionsAsync();
        Assert.Equal(18, descriptions.Count);
        Assert.DoesNotContain(descriptions, d => d.EndsWith('|'));
        Assert.Contains("18|Tạo, đổi tên, sửa quyền và xóa vai trò", descriptions);
    }

    /// <summary>
    /// ROLE-07 (GĐ6): Admin sửa quyền MODERATOR rồi seed lại HAI lần — chỉnh sửa còn nguyên (luật 3 của seeder), và dòng
    /// <c>role.manage</c> có mặt đúng một lần, có mô tả.
    /// </summary>
    [Fact]
    public async Task ROLE_07_seed_lai_hai_lan_sau_khi_sua_quyen_MODERATOR_khong_cap_lai_va_ma_18_mot_lan()
    {
        await SeedAsync();
        await ExecuteAsync($"delete from identity.role_permissions where role_id = 2 and permission_id = 6");

        await SeedAsync();
        await SeedAsync();
        var (_, permissions, grants) = await ReadSeedDataAsync();

        Assert.Equal(ExpectedGrants.Where(g => g != "2|6"), grants);
        Assert.Single(permissions, p => p == "18|role.manage");
        Assert.Contains("18|Tạo, đổi tên, sửa quyền và xóa vai trò", await ReadDescriptionsAsync());
    }

    [Fact]
    public async Task SEED_02_go_quyen_cua_MODERATOR_roi_seed_lai_khong_bi_cap_lai()
    {
        await SeedAsync();

        // Admin gỡ post.hide (6) của MODERATOR (2) lúc runtime.
        await ExecuteAsync($"delete from identity.role_permissions where role_id = 2 and permission_id = 6");

        await SeedAsync();
        var (_, _, grants) = await ReadSeedDataAsync();

        Assert.DoesNotContain("2|6", grants);
        Assert.Equal(ExpectedGrants.Where(g => g != "2|6"), grants);
    }

    /// <summary>
    /// Nửa còn lại của Mục 5.4: <c>roles</c> dùng <c>DO NOTHING</c> chứ không <c>DO UPDATE</c>, nên tên hiển thị
    /// Admin đã đổi không bị seeder ghi đè lại.
    /// </summary>
    [Fact]
    public async Task Seed_lai_khong_ghi_de_display_name_Admin_da_sua()
    {
        await SeedAsync();
        await ExecuteAsync($"update identity.roles set display_name = 'Điều phối viên' where role_id = 2");

        await SeedAsync();
        var (roles, _, _) = await ReadSeedDataAsync();

        Assert.Contains("2|MODERATOR|Điều phối viên", roles);
    }

    /// <summary>
    /// SEED-03 ở tầng seeder (A5): đổi <c>roles.code</c> của ADMIN bằng tay rồi seed lại thì bị từ chối, thông
    /// báo nêu đúng tên vai trò thiếu. Phần "app từ chối khởi động" — exit code khác 0 ở <c>--migrate</c> —
    /// nghiệm thu ở A6.
    ///
    /// Kiểm cả KIỂU ngoại lệ, không chỉ "có ném": nếu <c>roles</c> dùng <c>ON CONFLICT (code)</c> thì câu INSERT
    /// chết trước vì <c>role_id = 3</c> vẫn xung đột, ném lỗi Postgres, và kiểm tra vai trò không bao giờ
    /// chạy tới để nói ra nguyên nhân.
    /// </summary>
    ///
    /// GĐ6 (A3): từ khi có trigger <c>trg_roles_protect_system</c>, câu <c>UPDATE … 'ROOT'</c> bị chặn ngay lúc ghi (lớp 3). Test
    /// tắt đúng trigger đó trước — mô phỏng "ai đó đã DROP TRIGGER" — vì kiểm tra lúc khởi động là LỚP 2 và vẫn phải tự bắt được
    /// khi lớp 3 bị gỡ. KHÔNG dùng <c>session_replication_role = replica</c>: nó tắt cả trigger FK hệ thống.
    [Fact]
    public async Task SEED_03_doi_code_ADMIN_bang_tay_thi_seed_lai_bi_tu_choi_neu_ten_vai_tro_thieu()
    {
        await SeedAsync();
        await ExecuteAsync($"alter table identity.roles disable trigger trg_roles_protect_system");
        await ExecuteAsync($"update identity.roles set code = 'ROOT' where code = 'ADMIN'");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(SeedAsync);

        Assert.StartsWith("Thiếu vai trò hệ thống: ADMIN.", ex.Message);
    }

    /// <summary>
    /// FK-01 (Mục 10.1): xóa vai trò đang có người dùng bị DB từ chối. IdentityDbContextSchemaTests đã khóa
    /// delete_rule = RESTRICT ở tầng schema; test này khóa HÀNH VI — biện pháp #1 thay cho is_system (Mục 3.4).
    ///
    /// So SqlState, không so message: message của Postgres đổi theo locale và version, 23503 thì không.
    ///
    /// GĐ6 (A3): dùng vai trò TỰ TẠO (id 100) thay cho USER. Với ba vai trò hệ thống, trigger <c>trg_roles_protect_system</c> chặn
    /// <c>DELETE</c> trước khi FK kịp kiểm (P0001, không phải 23503) — ca đó là ROLE-05. Ở đây FK RESTRICT phải tự đứng được.
    /// </summary>
    [Fact]
    public async Task FK_01_xoa_vai_tro_dang_co_nguoi_dung_bi_tu_choi()
    {
        await SeedAsync();
        await ExecuteAsync($"insert into identity.roles (role_id, code, display_name) values (100, 'REVIEWER', 'Người duyệt')");
        await ExecuteAsync($"""
            insert into identity.users (user_id, email, password_hash, role_id)
            values ({Guid.NewGuid()}, 'fk01@test.local', 'khong-phai-hash-that', 100)
            """);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync($"delete from identity.roles where role_id = 100"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);   // 23503

        var (roles, _, _) = await ReadSeedDataAsync();
        Assert.Contains("100|REVIEWER|Người duyệt", roles);
    }

    private async Task SeedAsync()
    {
        // Mỗi lần seed một scope/DbContext mới — giống hai lần deploy độc lập.
        await using var scope = _services.CreateAsyncScope();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.ExecuteSqlAsync(sql);
    }

    private async Task<(List<string> Roles, List<string> Permissions, List<string> Grants)> ReadSeedDataAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var roles = await db.Database
            .SqlQuery<string>($"""select role_id || '|' || code || '|' || display_name as "Value" from identity.roles order by role_id""")
            .ToListAsync();
        var permissions = await db.Database
            .SqlQuery<string>($"""select permission_id || '|' || code as "Value" from identity.permissions order by permission_id""")
            .ToListAsync();
        var grants = await db.Database
            .SqlQuery<string>($"""select role_id || '|' || permission_id as "Value" from identity.role_permissions order by role_id, permission_id""")
            .ToListAsync();

        return (roles, permissions, grants);
    }

    /// <summary><c>permission_id|description</c>; mô tả NULL ra chuỗi kết thúc bằng <c>|</c>.</summary>
    private async Task<List<string>> ReadDescriptionsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database
            .SqlQuery<string>($"""select permission_id || '|' || coalesce(description, '') as "Value" from identity.permissions order by permission_id""")
            .ToListAsync();
    }
}
