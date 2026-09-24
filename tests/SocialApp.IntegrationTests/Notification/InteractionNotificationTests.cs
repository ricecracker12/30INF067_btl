using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Realtime;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Notification;

/// <summary>
/// Bước 9 của GĐ6 — thông báo <c>comment</c>, <c>reply</c>, <c>reaction</c> (từ Content, GĐ3) và <c>message</c> (từ Messaging, GĐ5), mỗi loại
/// đi từ <b>API thật của module phát</b> tới dòng <c>notification.notifications</c>, chờ bằng <see cref="EventBusHarness.DrainEventsAsync"/>.
///
/// Redis thật: <c>message</c> chỉ tạo khi người nhận offline — "online" là có kết nối hub, cần vé realtime và presence trong Redis.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InteractionNotificationTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
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

    private sealed record Dong(
        string Type, string GroupKey, string TargetType, Guid TargetId, Guid? PostId, Guid? LastActor, int ActorCount,
        DateTimeOffset UpdatedAt);

    private async Task<List<Dong>> ThongBaoCuaAsync(Guid recipient)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select type, group_key, target_type, target_id, post_id, last_actor_id, actor_count, updated_at " +
            "from notification.notifications where recipient_id = $1 order by created_at, id", conn);
        cmd.Parameters.AddWithValue(recipient);
        var rows = new List<Dong>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Dong(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetInt32(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        return rows;
    }

    private static async Task<Guid> NguoiAsync(ModulesTestClient client, string ten)
    {
        var id = Guid.NewGuid();
        await client.PutProfileOkAsync(id, new { displayName = ten });
        return id;
    }

    private static async Task<Guid> BaiAsync(ModulesTestClient client, Guid author) =>
        (await client.CreatePostOkAsync(author, new { body = "Bài để tương tác.", privacy = "public", mediaKeys = Array.Empty<object>() }))
        .PostId;

    // ---------------------------------------------------------------- comment / reply

    /// <summary>
    /// <c>comment</c>: B rồi C bình luận bài của A → A có MỘT nhóm <c>comment:post:{P}</c>, đích là bài, đếm 2, người cuối là C. A tự bình
    /// luận bài mình → không đổi gì.
    /// </summary>
    [Fact]
    public async Task Binh_luan_bai_bao_tac_gia_bai_gop_theo_bai()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài");
        var b = await NguoiAsync(client, "Người bình luận B");
        var c = await NguoiAsync(client, "Người bình luận C");
        var p = await BaiAsync(client, a);

        await client.CreateCommentOkAsync(b, p, "Hay quá.");
        await client.CreateCommentOkAsync(c, p, "Đồng ý.");
        await client.CreateCommentOkAsync(a, p, "Cảm ơn mọi người.");
        await factory.DrainEventsAsync();

        var dong = Assert.Single(await ThongBaoCuaAsync(a));
        Assert.Equal(("comment", $"comment:post:{p:D}", "post", p, (Guid?)p, (Guid?)c, 2),
            (dong.Type, dong.GroupKey, dong.TargetType, dong.TargetId, dong.PostId, dong.LastActor, dong.ActorCount));
        Assert.Empty(await ThongBaoCuaAsync(b));
    }

    /// <summary>
    /// <c>reply</c>: C trả lời bình luận của B trên bài của A → B có <c>reply:comment:{bình luận của B}</c> (đích là bình luận đó, kèm bài),
    /// A có <c>comment</c> của C. B tự trả lời bình luận của mình → B không nhận gì thêm, A được đếm thêm B.
    /// </summary>
    [Fact]
    public async Task Tra_loi_bao_tac_gia_binh_luan_cha_va_tac_gia_bai()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài R");
        var b = await NguoiAsync(client, "Người có bình luận cha");
        var c = await NguoiAsync(client, "Người trả lời");
        var p = await BaiAsync(client, a);
        var cha = (await client.CreateCommentOkAsync(b, p, "Bình luận gốc.")).CommentId;
        await factory.DrainEventsAsync();

        await client.CreateCommentOkAsync(c, p, "Trả lời B.", cha);
        await client.CreateCommentOkAsync(b, p, "B tự trả lời mình.", cha);
        await factory.DrainEventsAsync();

        var traLoi = Assert.Single(await ThongBaoCuaAsync(b));
        Assert.Equal(("reply", $"reply:comment:{cha:D}", "comment", cha, (Guid?)p, (Guid?)c, 1),
            (traLoi.Type, traLoi.GroupKey, traLoi.TargetType, traLoi.TargetId, traLoi.PostId, traLoi.LastActor, traLoi.ActorCount));

        var choA = Assert.Single(await ThongBaoCuaAsync(a));
        Assert.Equal(("comment", (Guid?)b, 2), (choA.Type, choA.LastActor, choA.ActorCount));
    }

    /// <summary>B trả lời bình luận CỦA A trên bài của A → A nhận đúng MỘT thông báo (<c>reply</c>), không kèm <c>comment</c>.</summary>
    [Fact]
    public async Task Tra_loi_binh_luan_cua_tac_gia_bai_chi_mot_thong_bao()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài T");
        var b = await NguoiAsync(client, "Người trả lời chủ bài");
        var p = await BaiAsync(client, a);
        var cuaA = (await client.CreateCommentOkAsync(a, p, "Chủ bài mở lời.")).CommentId;

        await client.CreateCommentOkAsync(b, p, "Trả lời chủ bài.", cuaA);
        await factory.DrainEventsAsync();

        var dong = Assert.Single(await ThongBaoCuaAsync(a));
        Assert.Equal(("reply", cuaA, (Guid?)b), (dong.Type, dong.TargetId, dong.LastActor));
    }

    // ---------------------------------------------------------------- reaction

    /// <summary><c>NOTIF-02</c> (Mục 10.1): tự thả cảm xúc bài mình → không thông báo.</summary>
    [Fact]
    public async Task NOTIF_02_tu_tha_cam_xuc_bai_minh_khong_thong_bao()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Tự thả tim");
        var p = await BaiAsync(client, a);

        await client.ReactOkAsync(a, "posts", p, "love");
        await factory.DrainEventsAsync();

        Assert.Empty(await ThongBaoCuaAsync(a));
    }

    /// <summary><c>NOTIF-03</c> qua API: B, C, D thả cảm xúc bài của A → một dòng, đếm 3, người cuối là D.</summary>
    [Fact]
    public async Task NOTIF_03_ba_nguoi_tha_cam_xuc_mot_dong_dem_ba()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài C3");
        var p = await BaiAsync(client, a);
        Guid[] nguoi = [await NguoiAsync(client, "B3"), await NguoiAsync(client, "C3"), await NguoiAsync(client, "D3")];

        foreach (var n in nguoi)
        {
            await client.ReactOkAsync(n, "posts", p, "like");
            await factory.DrainEventsAsync();
        }

        var dong = Assert.Single(await ThongBaoCuaAsync(a));
        Assert.Equal(("reaction", $"reaction:post:{p:D}", "post", (Guid?)nguoi[2], 3),
            (dong.Type, dong.GroupKey, dong.TargetType, dong.LastActor, dong.ActorCount));
    }

    /// <summary>
    /// <c>NOTIF-04</c> qua API: B thả, gỡ, thả lại → vẫn đếm 1. Rồi B ĐỔI loại (like → haha) → không phải "có gì mới": dòng không đổi, kể
    /// cả <c>updated_at</c> (không nhảy lên đầu danh sách).
    /// </summary>
    [Fact]
    public async Task NOTIF_04_tha_go_tha_lai_dem_mot_doi_loai_khong_lam_gi()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài C4");
        var b = await NguoiAsync(client, "B4");
        var p = await BaiAsync(client, a);

        await client.ReactOkAsync(b, "posts", p, "like");
        using (var go = await client.ReactAsync(b, "posts", p, null))
            Assert.True(go.IsSuccessStatusCode);
        await client.ReactOkAsync(b, "posts", p, "like");
        await factory.DrainEventsAsync();
        var truoc = Assert.Single(await ThongBaoCuaAsync(a));
        Assert.Equal(1, truoc.ActorCount);

        await client.ReactOkAsync(b, "posts", p, "haha");
        await factory.DrainEventsAsync();

        Assert.Equal(truoc, Assert.Single(await ThongBaoCuaAsync(a)));
    }

    /// <summary>Cảm xúc trên BÌNH LUẬN → tác giả bình luận nhận nhóm <c>reaction:comment:{id}</c>, đích là bình luận, kèm bài.</summary>
    [Fact]
    public async Task Cam_xuc_tren_binh_luan_bao_tac_gia_binh_luan()
    {
        var client = new ModulesTestClient(factory);
        var a = await NguoiAsync(client, "Chủ bài C5");
        var b = await NguoiAsync(client, "Tác giả bình luận C5");
        var c = await NguoiAsync(client, "Người thả C5");
        var p = await BaiAsync(client, a);
        var binhLuan = (await client.CreateCommentOkAsync(b, p, "Bình luận được thả tim.")).CommentId;

        await client.ReactOkAsync(c, "comments", binhLuan, "wow");
        await factory.DrainEventsAsync();

        var dong = Assert.Single(await ThongBaoCuaAsync(b));
        Assert.Equal(("reaction", $"reaction:comment:{binhLuan:D}", "comment", binhLuan, (Guid?)p, (Guid?)c),
            (dong.Type, dong.GroupKey, dong.TargetType, dong.TargetId, dong.PostId, dong.LastActor));
    }

    // ---------------------------------------------------------------- message

    /// <summary>
    /// <c>message</c> (Đ-6.17): B offline → A gửi hai tin qua REST → B có MỘT nhóm <c>message:{hội thoại}</c>, đích là hội thoại, actor A,
    /// đếm 1. A (người gửi) không nhận gì loại <c>message</c>.
    /// </summary>
    [Fact]
    public async Task Tin_nhan_khi_nguoi_nhan_offline_tao_thong_bao()
    {
        var messaging = new MessagingTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await messaging.FriendsAsync(a, b);
        var hoiThoai = (await messaging.OpenOkAsync(a, b)).ConversationId;

        await messaging.SendOkAsync(a, hoiThoai, "Chào B.");
        await messaging.SendOkAsync(a, hoiThoai, "B có đó không?");
        await factory.DrainEventsAsync();

        var tin = Assert.Single(await ThongBaoCuaAsync(b), d => d.Type == "message");
        Assert.Equal(($"message:{hoiThoai:D}", "conversation", hoiThoai, (Guid?)null, (Guid?)a, 1),
            (tin.GroupKey, tin.TargetType, tin.TargetId, tin.PostId, tin.LastActor, tin.ActorCount));
        Assert.DoesNotContain(await ThongBaoCuaAsync(a), d => d.Type == "message");
    }

    /// <summary>B đang kết nối hub (online) → A gửi tin → KHÔNG thông báo <c>message</c>: B đã thấy tin qua hub và badge của màn chat.</summary>
    [Fact]
    public async Task Tin_nhan_khi_nguoi_nhan_online_khong_tao_thong_bao()
    {
        var messaging = new MessagingTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await messaging.FriendsAsync(a, b);
        var hoiThoai = (await messaging.OpenOkAsync(a, b)).ConversationId;

        await using var hub = await RealtimeTestClient.ConnectAsync(factory, messaging.Modules.Http, b);
        var presence = factory.Service<IPresenceReader>();
        var hanChot = DateTime.UtcNow.AddSeconds(5);
        while (!await presence.IsOnlineAsync(b))
        {
            Assert.True(DateTime.UtcNow < hanChot, "B không online sau 5 giây kết nối hub.");
            await Task.Delay(20);
        }

        await messaging.SendOkAsync(a, hoiThoai, "B đang online.");
        await factory.DrainEventsAsync();

        Assert.DoesNotContain(await ThongBaoCuaAsync(b), d => d.Type == "message");
    }
}
