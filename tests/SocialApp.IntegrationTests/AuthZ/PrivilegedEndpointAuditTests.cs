using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// C4 (GĐ6) với Redis THẬT: <c>AUD-03</c> (Đ-6.15, US-019 AC-03) — tầng 2 từ chối một endpoint <c>[PrivilegedEndpoint]</c> thì ghi
/// ĐÚNG một dòng <c>access.denied</c> mỗi phút cho mỗi (người, route template); và <c>ANY-01</c> — <c>[RequireAnyPermission]</c>.
///
/// Chạy trên probe controller (<see cref="AuthZProbeController"/>): lúc C4 chưa có controller admin/moderation thật nào. Redis thật
/// vì: (1) chống ngập là <c>SET NX EX 60</c>; (2) Redis không tới được thì endpoint đặc quyền đã 503 trước khi tầng 2 kịp từ chối
/// (<see cref="FailClosedTests"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PrivilegedEndpointAuditTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        factory.UseTestServices(s => s.AddControllers().AddApplicationPart(typeof(AuthZApiFactory).Assembly));
        await factory.UseFreshDatabaseAsync(postgres);

        // Kết nối Redis của APP mở ở nền lúc host khởi động — chưa xong thì kiểm thu hồi ra Unknown và probe đặc quyền 503.
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpStatusCode> GetAsync(string path, Guid? userId, string role = "USER")
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (userId is { } id)
            request.Headers.Authorization = ModulesTestClient.Bearer(id, role);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<List<string>> DeniedRowsAsync(Guid actorId)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select target_type || '|' || (metadata->>'method') || '|' || (metadata->>'routeTemplate') || '|' || metadata::text "
          + "from moderation.audit_logs where action = 'access.denied' and actor_id = $1", conn);
        cmd.Parameters.AddWithValue(actorId);
        var rows = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }

    /// <summary>
    /// AUD-03: USER gọi endpoint đặc quyền 5 lần trong một phút, MỖI LẦN MỘT ID khác trên đường → 5 × 403, ĐÚNG MỘT dòng audit.
    /// Khóa chống ngập theo route TEMPLATE (theo <c>Request.Path</c> thì ra 5 dòng); metadata không chứa id nào trên đường.
    /// </summary>
    [Fact]
    public async Task AUD_03_nam_lan_bi_tu_choi_cung_route_trong_mot_phut_dung_mot_dong_audit()
    {
        var user = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
            Assert.Equal(HttpStatusCode.Forbidden, await GetAsync($"/__test/authz/privileged/{id}", user));

        var row = Assert.Single(await DeniedRowsAsync(user));
        Assert.StartsWith("endpoint|GET|__test/authz/privileged/{id:guid}|", row);
        Assert.All(ids, id => Assert.DoesNotContain(id.ToString(), row, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// AUD-03b: KHÔNG ghi audit khi (a) endpoint không đặc quyền bị 403, (b) chưa đăng nhập (401 — không có actor, cửa làm ngập),
    /// (c) đặc quyền và được phép (MODERATOR có report.resolve → 200).
    /// </summary>
    [Fact]
    public async Task AUD_03b_khong_dac_quyen_an_danh_va_duoc_phep_thi_khong_ghi_audit()
    {
        var user = Guid.NewGuid();
        var moderator = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync("/__test/authz/user-lock", user));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync($"/__test/authz/privileged/{Guid.NewGuid()}", null));
        Assert.Equal(HttpStatusCode.OK, await GetAsync($"/__test/authz/privileged/{Guid.NewGuid()}", moderator, "MODERATOR"));

        Assert.Empty(await DeniedRowsAsync(user));
        Assert.Empty(await DeniedRowsAsync(moderator));
    }

    /// <summary>
    /// ANY-01 (Mục 6.1): vai trò tự tạo CHỈ có <c>role.assign</c> qua <c>[RequireAnyPermission("user.lock","user.unlock","role.assign")]</c>;
    /// USER (không mã nào) bị 403; ADMIN qua bằng short-circuit.
    /// </summary>
    [Fact]
    public async Task ANY_01_co_mot_trong_cac_quyen_thi_qua()
    {
        await using (var conn = new NpgsqlConnection(factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                insert into identity.roles (role_id, code, display_name) values (100, 'HR', 'Nhân sự');
                insert into identity.role_permissions (role_id, permission_id)
                select 100, permission_id from identity.permissions where code = 'role.assign';
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.OK, await GetAsync("/__test/authz/privileged-any", Guid.NewGuid(), "HR"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync("/__test/authz/privileged-any", Guid.NewGuid(), "USER"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("/__test/authz/privileged-any", Guid.NewGuid(), "ADMIN"));
    }
}
