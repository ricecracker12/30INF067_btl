using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Notification;

/// <summary>
/// GĐ6 D10 — ba handler "làm ngay" của Đ-6.17, mỗi cái chứng minh từ <b>API thật của module phát</b> tới dòng
/// <c>notification.notifications</c>: lời mời qua <c>socialgraph-v1</c>, ẩn bài qua <c>PATCH /reports</c> của <c>moderation-v1</c>. Không
/// <c>Publish</c> tay, không <c>Task.Delay</c> — chờ bằng <see cref="EventBusHarness.DrainEventsAsync"/> (Đ-6.2).
///
/// Redis thật: <c>PATCH /reports</c> là endpoint <c>[PrivilegedEndpoint]</c>, fail-closed khi không kiểm được thu hồi (Đ-6.8).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NotificationHandlerTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Dong(
        Guid Id, string Type, string GroupKey, string TargetType, Guid TargetId, Guid? PostId, Guid? LastActor, int ActorCount,
        string? ReasonCode, bool IsRead);

    private async Task<List<Dong>> ThongBaoCuaAsync(Guid recipient)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select id, type, group_key, target_type, target_id, post_id, last_actor_id, actor_count, reason_code, is_read " +
            "from notification.notifications where recipient_id = $1 order by created_at, id", conn);
        cmd.Parameters.AddWithValue(recipient);
        var rows = new List<Dong>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Dong(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9)));
        return rows;
    }

    private async Task<List<Guid>> NguoiCuaAsync(Guid notificationId)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select actor_id from notification.notification_actors where notification_id = $1 order by actor_id", conn);
        cmd.Parameters.AddWithValue(notificationId);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task<Guid> NguoiCoHoSoAsync(ModulesTestClient client, string ten)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = ten });
        return id;
    }

    /// <summary>
    /// <c>NOTIF-01</c>: A mời B qua API → B có <c>friend_request</c> (actor A, đích là A); B chấp nhận → A có <c>friend_accepted</c> (actor
    /// B, đích là B). Không ai nhận thông báo về hành động của CHÍNH mình.
    /// </summary>
    [Fact]
    public async Task NOTIF_01_moi_roi_chap_nhan_moi_ben_mot_thong_bao_dung_vai()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiCoHoSoAsync(client, "Người mời");
        var b = await NguoiCoHoSoAsync(client, "Người được mời");

        await client.SendFriendRequestOkAsync(a, b);
        await factory.DrainEventsAsync();

        var loiMoi = Assert.Single(await ThongBaoCuaAsync(b));
        Assert.Equal(("friend_request", $"friend_request:{a:D}", "user", a, (Guid?)null, (Guid?)a, 1, (string?)null, false),
            (loiMoi.Type, loiMoi.GroupKey, loiMoi.TargetType, loiMoi.TargetId, loiMoi.PostId, loiMoi.LastActor, loiMoi.ActorCount,
                loiMoi.ReasonCode, loiMoi.IsRead));
        Assert.Equal([a], await NguoiCuaAsync(loiMoi.Id));
        Assert.Empty(await ThongBaoCuaAsync(a));

        await client.AcceptOkAsync(b, a);
        await factory.DrainEventsAsync();

        var chapNhan = Assert.Single(await ThongBaoCuaAsync(a));
        Assert.Equal(("friend_accepted", $"friend_accepted:{b:D}", "user", b, (Guid?)b, 1),
            (chapNhan.Type, chapNhan.GroupKey, chapNhan.TargetType, chapNhan.TargetId, chapNhan.LastActor, chapNhan.ActorCount));
        Assert.Equal([b], await NguoiCuaAsync(chapNhan.Id));
        Assert.Single(await ThongBaoCuaAsync(b));   // người bấm chấp nhận không nhận gì thêm
    }

    /// <summary>
    /// <c>NOTIF-01b</c> (đề xuất): mời → B đọc → A hủy → A mời lại → vẫn MỘT dòng <c>friend_request</c>, sáng lại (chưa đọc), đếm 1 —
    /// cùng người, đợt mới. Không rút lại thông báo khi hủy (Đ-6.16).
    /// </summary>
    [Fact]
    public async Task NOTIF_01b_moi_huy_moi_lai_van_mot_dong_sang_lai()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiCoHoSoAsync(client, "Người mời lại");
        var b = await NguoiCoHoSoAsync(client, "Người được mời lại");

        await client.SendFriendRequestOkAsync(a, b);
        await factory.DrainEventsAsync();
        var dau = Assert.Single(await ThongBaoCuaAsync(b));
        Assert.Equal(1, await client.ExecuteSqlAsync("update notification.notifications set is_read = true where id = $1", dau.Id));

        await client.DeclineOrCancelOkAsync(a, b);
        await factory.DrainEventsAsync();
        Assert.True(Assert.Single(await ThongBaoCuaAsync(b)).IsRead);   // hủy không phát event, không rút thông báo

        await client.SendFriendRequestOkAsync(a, b);
        await factory.DrainEventsAsync();

        var sau = Assert.Single(await ThongBaoCuaAsync(b));
        Assert.Equal((dau.Id, false, 1, (Guid?)a), (sau.Id, sau.IsRead, sau.ActorCount, sau.LastActor));
    }

    /// <summary>
    /// <c>NOTIF-09</c> (Đ-6.17, B.10 #7): R báo bài của C → Moderator M <c>hide</c> qua <c>PATCH /reports</c> với lý do của CHÍNH quyết định →
    /// C có thông báo <c>moderation</c>: <c>last_actor_id</c> NULL, <c>reason_code</c> là lý do của quyết định, 0 dòng
    /// <c>notification_actors</c>. Id của M lẫn của R không xuất hiện ở BẤT KỲ cột nào của cả hai bảng (so trên dạng chữ của cả dòng,
    /// gồm cả <c>group_key</c>), và cả hai không nhận thông báo nào.
    /// </summary>
    [Fact]
    public async Task NOTIF_09_an_bai_tac_gia_co_thong_bao_kiem_duyet_khong_lo_ai()
    {
        var client = new ModulesTestClient(factory);
        var c = await NguoiCoHoSoAsync(client, "Tác giả bị ẩn bài");
        var p = (await client.CreatePostOkAsync(c, new { body = "Bài sẽ bị ẩn.", privacy = "public", mediaKeys = Array.Empty<object>() }))
            .PostId;
        var r = Guid.NewGuid();
        Guid reportId;
        using (var report = await client.CreateReportAsync(r, new { targetType = "post", targetId = p, reasonCode = "spam" }))
        {
            Assert.Equal(HttpStatusCode.Created, report.StatusCode);
            using var body = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
            reportId = body.RootElement.GetProperty("reportId").GetGuid();
        }

        var m = Guid.NewGuid();
        using (var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/reports/{reportId}")
        {
            Content = JsonContent.Create(new { decision = "hide", reasonCode = "violence", note = "Ghi chú của Moderator." }),
        })
        {
            request.Headers.Authorization = ModulesTestClient.Bearer(m, "MODERATOR");
            using var decided = await client.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        }

        await factory.DrainEventsAsync();

        var dong = Assert.Single(await ThongBaoCuaAsync(c));
        Assert.Equal(("moderation", $"moderation:post:{p:D}", "post", p, (Guid?)p, (Guid?)null, 1, (string?)"violence", false),
            (dong.Type, dong.GroupKey, dong.TargetType, dong.TargetId, dong.PostId, dong.LastActor, dong.ActorCount, dong.ReasonCode,
                dong.IsRead));
        Assert.Empty(await NguoiCuaAsync(dong.Id));

        foreach (var ai in new[] { m, r })
        {
            var lo = await client.QueryRowAsync(
                "select (select count(*) from notification.notifications n where n::text like '%' || $1 || '%') " +
                "     + (select count(*) from notification.notification_actors a where a::text like '%' || $1 || '%') as n",
                ai.ToString("D"));
            Assert.Equal(0L, (long)lo!["n"]!);
        }
    }
}
