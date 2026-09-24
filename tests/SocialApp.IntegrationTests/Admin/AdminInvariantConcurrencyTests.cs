using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Admin;

/// <summary>
/// Mốc 2 của GĐ6 (Đ-6.7, R6-05): không đường nào về 0 Admin hoạt động KỂ CẢ dưới đồng thời. Lớp riêng, database riêng: mỗi lượt
/// đưa tập Admin về đúng hai người, nên không được chung database với ca nào khác.
///
/// "Đồng thời" thật ở tầng DB (cạm bẫy 5 của D3): hai Admin, hai token, hai <see cref="HttpClient"/>, <c>Task.WhenAll</c>. Một client
/// một token thì hai request vẫn chạy song song trong TestServer, nhưng đó không phải kịch bản "hai Admin khóa nhau".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminInvariantConcurrencyTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private const int Rounds = 20;

    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    private async Task<long> ActiveAdminsAsync()
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM identity.users u JOIN identity.roles r ON r.role_id = u.role_id
             WHERE r.code = 'ADMIN' AND u.status = 'active'
            """, conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Tập Admin hoạt động về đúng rỗng — lượt sau tự dựng hai Admin của nó.</summary>
    private async Task DisableAllAdminsAsync()
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE identity.users SET status = 'disabled'
             WHERE role_id = (SELECT role_id FROM identity.roles WHERE code = 'ADMIN')
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<HttpStatusCode> LockAsync(HttpClient http, Guid actor, Guid target)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{target}/lock")
        {
            Content = JsonContent.Create(new { reason = "ADM-C2" }),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(actor, "ADMIN");
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// ADM-C2 ⭐: đúng hai Admin X, Y; X khóa Y ‖ Y khóa X, <see cref="Rounds"/> lượt liền trong một ca. Mỗi lượt: luôn còn ĐÚNG một
    /// Admin hoạt động, và đúng MỘT bên 200.
    ///
    /// Bên thua thường nhận 409 <c>last-admin</c> (khóa tư vấn xếp hàng nó sau bên thắng, đếm sau khi ghi thấy 0). Nó cũng có thể nhận
    /// 401: nếu request của nó tới tầng 1 SAU khi bên thắng đã ghi <c>revoked:user</c> — bị khóa rồi thì không còn quyền gọi. Cả hai
    /// đều giữ bất biến; thứ KHÔNG được xảy ra là hai 200 (bỏ khóa tư vấn, hay đếm trước khi ghi — đột biến M2, M3 của D3).
    /// </summary>
    [Fact]
    public async Task ADM_C2_hai_Admin_khoa_nhau_dong_thoi_luon_con_mot_Admin_20_luot()
    {
        var loserCodes = new List<HttpStatusCode>();
        for (var round = 0; round < Rounds; round++)
        {
            await DisableAllAdminsAsync();
            var x = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);
            var y = await IdentitySql.TaoAdminThuHaiAsync(factory.ConnectionString);
            Assert.Equal(2, await ActiveAdminsAsync());

            using var httpX = factory.CreateClient();
            using var httpY = factory.CreateClient();
            var codes = await Task.WhenAll(LockAsync(httpX, x, y), LockAsync(httpY, y, x));

            Assert.True(await ActiveAdminsAsync() == 1,
                $"lượt {round + 1}: còn {await ActiveAdminsAsync()} Admin hoạt động, mã [{string.Join(", ", codes.Select(c => (int)c))}]");
            Assert.True(codes.Count(c => c == HttpStatusCode.OK) == 1,
                $"lượt {round + 1}: mã [{string.Join(", ", codes.Select(c => (int)c))}] — phải đúng một 200");
            var loser = codes.Single(c => c != HttpStatusCode.OK);
            Assert.True(loser is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized, $"lượt {round + 1}: bên thua {(int)loser}");
            loserCodes.Add(loser);
        }

        // Bằng chứng ca này thật sự chạm nhánh 409 (không chỉ toàn 401 do lệch thời điểm): phần lớn lượt phải là 409.
        Assert.True(loserCodes.Count(c => c == HttpStatusCode.Conflict) >= Rounds / 2,
            $"409: {loserCodes.Count(c => c == HttpStatusCode.Conflict)}/{Rounds}, 401: {loserCodes.Count(c => c == HttpStatusCode.Unauthorized)}");
    }
}
