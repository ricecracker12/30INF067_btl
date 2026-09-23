using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.SharedKernel.Contracts;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// C5 (GĐ6) — <see cref="IAccountStatusReader"/> trên Postgres thật, khuôn <see cref="UserDirectoryTests"/>. Bản đầy đủ
/// <c>SRCH-05</c> (người bị khóa không hiện trong tìm kiếm) là của D12.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountStatusReaderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection().AddIdentityModule(_connectionString).BuildServiceProvider();
        await _services.MigrateIdentityModuleAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_connectionString);
        NpgsqlConnection.ClearPool(conn);
    }

    private async Task<Guid> InsertUserAsync(string status)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into identity.users (user_id, email, password_hash, role_id, status) values ($1, $2, 'khong-phai-hash', 1, $3)", conn);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue($"{id:N}@test.local");
        cmd.Parameters.AddWithValue(status);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<IReadOnlySet<Guid>> InactiveAsync(IReadOnlyCollection<Guid> ids)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountStatusReader>().GetInactiveAsync(ids);
    }

    /// <summary>Active, disabled, deleted, và id không tồn tại → chỉ hai tài khoản không hoạt động có mặt.</summary>
    [Fact]
    public async Task Chi_tra_tai_khoan_khong_hoat_dong_id_la_vang_mat()
    {
        var active = await InsertUserAsync("active");
        var disabled = await InsertUserAsync("disabled");
        var deleted = await InsertUserAsync("deleted");
        var missing = Guid.NewGuid();

        var inactive = await InactiveAsync([active, disabled, deleted, missing]);

        Assert.Equal(new HashSet<Guid> { disabled, deleted }, inactive);
    }

    /// <summary>Batch thật: 50 id là MỘT câu SQL — ô tìm kiếm gọi hàm này mỗi lần gõ phím. Danh sách rỗng không chạm DB.</summary>
    [Fact]
    public async Task Nam_muoi_id_mot_cau_SQL_danh_sach_rong_khong_cau_nao()
    {
        var ids = new List<Guid> { await InsertUserAsync("disabled") };
        ids.AddRange(Enumerable.Range(0, 49).Select(_ => Guid.NewGuid()));

        using var counter = new SqlCommandCounter(_connectionString);
        Assert.Single(await InactiveAsync(ids));
        Assert.Single(counter.Statements);

        counter.Reset();
        Assert.Empty(await InactiveAsync([]));
        Assert.Empty(counter.Statements);
    }
}
