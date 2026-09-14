using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.AuthZ;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Nghiệm thu C3 trên dữ liệu thật và bằng chứng Mục 6.7.2 "nâng cấp là thay dữ liệu, không thay code":
/// gỡ post.hide của MODERATOR bằng SQL → còn hạn cache thì vẫn 200 → đồng hồ giả qua 61 giây → 403. Không dòng
/// code nào đổi giữa hai lần gọi.
///
/// Test SỬA dữ liệu → database riêng (CreateDatabaseAsync), không đụng DB "authz" của matrix. Đồng hồ giả thay
/// qua ConfigureTestServices trên một bản sao của AuthZApiFactory — không sửa factory (Đ3: không thay nguồn quyền).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PermissionDataDrivenTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Go_post_hide_cua_MODERATOR_trong_DB_thi_bi_403_sau_61_giay_khong_sua_code()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var services = new ServiceCollection().AddIdentityModule(connectionString).BuildServiceProvider())
            await services.MigrateIdentityModuleAsync();

        var time = new ManualTime();
        using var baseFactory = new AuthZApiFactory();
        baseFactory.UseDatabase(connectionString);
        using var factory = baseFactory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(time)));
        var client = factory.CreateClient();
        var token = TestJwt.Create("MODERATOR");

        // 1. Có quyền theo dữ liệu seed.
        Assert.Equal(HttpStatusCode.OK, await CallPostHideAsync());

        // 2. Admin gỡ post.hide (6) của MODERATOR (2) lúc runtime — cache còn hạn nên vẫn qua.
        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "delete from identity.role_permissions where role_id = 2 and permission_id = 6", conn);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        Assert.Equal(HttpStatusCode.OK, await CallPostHideAsync());

        // 3. Hết TTL 60 giây → đọc lại role_permissions → bị chặn.
        time.Now += TimeSpan.FromSeconds(61);
        Assert.Equal(HttpStatusCode.Forbidden, await CallPostHideAsync());

        async Task<HttpStatusCode> CallPostHideAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/authz/post-hide");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
    }

    /// <summary>Bắt đầu ở giờ thật: chỉ cache đọc TimeProvider này, token vẫn được validate theo giờ hệ thống.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
