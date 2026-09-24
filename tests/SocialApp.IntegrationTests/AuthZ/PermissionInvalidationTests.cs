using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// C3 (GĐ6, Đ-6.10) — sửa quyền của một vai trò có hiệu lực ở request kế tiếp, trên MỌI instance, không đợi TTL 60 giây.
/// Bản HẠ TẦNG (L-C2): sửa <c>role_permissions</c> bằng SQL + gọi <see cref="IPermissionChangeNotifier"/> — đúng hai bước D5 sẽ
/// làm sau <c>COMMIT</c>. Bản đầy đủ qua <c>PUT /admin/roles/{id}/permissions</c> là của D5.
///
/// Không tua <c>TimeProvider</c> ở ca nào: đồng hồ đứng yên thì chỉ invalidate mới làm cache đổi — đó chính là thứ cần chứng minh.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PermissionInvalidationTests(PostgresFixture postgres, RedisFixture redis)
    : IClassFixture<RedisFixture>
{
    private static readonly object Post = new { body = "Bài thử quyền.", privacy = "public" };

    private static async Task RevokePostCreateFromUserAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            delete from identity.role_permissions
            where role_id = (select role_id from identity.roles where code = 'USER')
              and permission_id = (select permission_id from identity.permissions where code = 'post.create')
            """, conn);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// PERM-01 + đối chứng trong MỘT ca, theo đúng thứ tự: cache đã nóng → gỡ <c>post.create</c> của USER trong DB → KHÔNG báo thì
    /// request kế tiếp vẫn 201 (cache còn — chứng minh ca này đo đúng thứ cần đo) → báo → request kế tiếp 403. Redis không tới được:
    /// xóa TẠI CHỖ không phụ thuộc Redis.
    /// </summary>
    [Fact]
    public async Task PERM_01_sua_quyen_roi_notify_thi_request_ke_tiep_thay_ngay_khong_doi_TTL()
    {
        await using var factory = new ModulesApiFactory();
        await factory.UseFreshDatabaseAsync(postgres);
        var client = new ModulesTestClient(factory);
        var user = Guid.NewGuid();
        await client.PutProfileOkAsync(user, new { displayName = "Người thử quyền" });

        Assert.Equal(HttpStatusCode.Created, (await client.CreatePostAsync(user, Post)).StatusCode);   // cache USER nóng

        await RevokePostCreateFromUserAsync(factory.ConnectionString);
        Assert.Equal(HttpStatusCode.Created, (await client.CreatePostAsync(user, Post)).StatusCode);   // đối chứng: chưa báo

        await factory.Services.GetRequiredService<IPermissionChangeNotifier>().NotifyAsync("USER");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.CreatePostAsync(user, Post)).StatusCode);
    }

    /// <summary>
    /// PERM-02: hai host chung DB + chung Redis thật (mô phỏng hai bản sao API của GĐ7). Báo ở host 1 → host 2 thấy quyền mới trong
    /// ≤ 1 giây qua pub/sub. Chờ bằng TRẠNG THÁI: trước khi báo, đợi Redis xác nhận cả hai host đã SUBSCRIBE kênh (PUBSUB NUMSUB);
    /// sau khi báo, hỏi lại host 2 tới khi 403 hoặc hết 1 giây.
    /// </summary>
    [Fact]
    public async Task PERM_02_notify_o_host_1_thi_host_2_thay_trong_1_giay()
    {
        await using var first = new ModulesApiFactory();
        first.UseRedis(redis.ConnectionString);
        await first.UseFreshDatabaseAsync(postgres);

        await using var second = new ModulesApiFactory();
        second.UseRedis(redis.ConnectionString);
        second.UseDatabase(first.ConnectionString);

        var client1 = new ModulesTestClient(first);
        var client2 = new ModulesTestClient(second);
        var user = Guid.NewGuid();
        await client1.PutProfileOkAsync(user, new { displayName = "Người thử quyền" });
        Assert.Equal(HttpStatusCode.Created, (await client2.CreatePostAsync(user, Post)).StatusCode);   // cache host 2 nóng

        await WaitForSubscribersAsync(PermissionsChangedChannel.For("Development"), expected: 2);

        await RevokePostCreateFromUserAsync(first.ConnectionString);
        await first.Services.GetRequiredService<IPermissionChangeNotifier>().NotifyAsync("USER");

        var clock = Stopwatch.StartNew();
        HttpStatusCode status;
        do
        {
            status = (await client2.CreatePostAsync(user, Post)).StatusCode;
            if (status == HttpStatusCode.Forbidden)
                break;
            await Task.Delay(50);
        }
        while (clock.Elapsed < TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.Forbidden, status);

        // Kết nối Redis của cả hai host đã mở (không thì ca này xanh vì lý do sai — không có pub/sub nào chạy).
        Assert.True((await second.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    private async Task WaitForSubscribersAsync(string channel, int expected)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            var reply = (RedisResult[])(await redis.Database.ExecuteAsync("PUBSUB", "NUMSUB", channel))!;
            if ((long)reply[1] >= expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Sau 10 giây kênh {channel} vẫn chưa đủ {expected} subscriber — subscriber không đăng ký được.");
    }
}
