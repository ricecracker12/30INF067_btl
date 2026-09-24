using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.DependencyInjection;
using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.IntegrationTests.Notification;

/// <summary>
/// GĐ6 D9 — upsert gộp thông báo (Đ-6.16, L-D6) ở tầng store, trên Postgres thật, gọi <see cref="INotificationStore"/> trực tiếp: handler
/// là D10, endpoint là D11. <c>NOTIF-03..05</c> ở đây là bản TẦNG STORE của các ca cùng tên trong Mục 10.1 — sự kiện dựng tay, không đi
/// qua event.
///
/// Đồng thời thật: mỗi lượt song song một scope (một DbContext, một kết nối) — khuôn <c>ConversationStoreTests</c>. Đồng hồ là của
/// lớp test để khẳng định được <c>updated_at</c> đổi, <c>created_at</c> đứng yên.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NotificationStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    private readonly ManualTime _clock = new() { Now = T0 };
    private ServiceProvider _services = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        // Trần pool 10 như ConversationStoreTests: 20 lượt song song vẫn tranh khóa thật, mà không đẩy container dùng chung
        // (max_connections = 100) sang 53300.
        _cs = new NpgsqlConnectionStringBuilder(await postgres.CreateDatabaseAsync()) { MaxPoolSize = 10 }.ConnectionString;
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_clock)   // TRƯỚC AddNotificationModule: module TryAdd đồng hồ hệ thống
            .AddNotificationModule(_cs)
            .BuildServiceProvider();
        await _services.MigrateNotificationModuleAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_cs);
        NpgsqlConnection.ClearPool(conn);
    }

    // ---------------------------------------------------------------- dựng cảnh

    private async Task<UpsertResult> UpsertAsync(NotificationUpsert upsert)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationStore>().UpsertAsync(upsert, default);
    }

    /// <summary>Một sự kiện cảm xúc trên bài <paramref name="post"/> của <paramref name="recipient"/> — nhóm <c>reaction:post:{post}</c>.</summary>
    private static NotificationUpsert CamXuc(Guid recipient, Guid post, Guid actor) => new(
        recipient, NotificationTypes.Reaction, GroupKey.Reaction(ReactionTargetKind.Post, post),
        NotificationTargetTypes.Post, post, post, actor, null);

    private static NotificationUpsert KiemDuyet(Guid author, Guid post, string reasonCode) => new(
        author, NotificationTypes.Moderation, GroupKey.Moderation(ModerationTargetType.Post, post),
        NotificationTargetTypes.Post, post, post, null, reasonCode);

    private sealed record Dong(
        Guid Id, int ActorCount, Guid? LastActor, bool IsRead, string? ReasonCode, string Type, string TargetType, Guid TargetId,
        Guid? PostId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    /// <summary>Mọi dòng của một người nhận (thường là một).</summary>
    private async Task<List<Dong>> DongCuaAsync(Guid recipient)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select id, actor_count, last_actor_id, is_read, reason_code, type, target_type, target_id, post_id, created_at, updated_at " +
            "from notification.notifications where recipient_id = $1 order by id", conn);
        cmd.Parameters.AddWithValue(recipient);
        var rows = new List<Dong>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Dong(
                reader.GetGuid(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        return rows;
    }

    private async Task<List<Guid>> NguoiCuaAsync(Guid notificationId)
    {
        await using var conn = new NpgsqlConnection(_cs);
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

    /// <summary>Người nhận "đọc" nhóm — SQL tay: endpoint đánh dấu đã đọc là D11.</summary>
    private async Task DanhDauDaDocAsync(Guid notificationId)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("update notification.notifications set is_read = true where id = $1", conn);
        cmd.Parameters.AddWithValue(notificationId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ---------------------------------------------------------------- một lượt

    /// <summary><c>NOTIF-02-store</c> (Đ-6.16): tự báo mình → <c>Skipped</c>, không dòng nào — kể cả khi người khác vừa tạo nhóm đó.</summary>
    [Fact]
    public async Task NOTIF_02_store_tu_bao_minh_khong_dong_nao()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();

        Assert.Equal(UpsertResult.Skipped, await UpsertAsync(CamXuc(an, bai, an)));
        Assert.Empty(await DongCuaAsync(an));

        var binh = Guid.NewGuid();
        await UpsertAsync(CamXuc(an, bai, binh));
        Assert.Equal(UpsertResult.Skipped, await UpsertAsync(CamXuc(an, bai, an)));

        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal((1, (Guid?)binh), (dong.ActorCount, dong.LastActor));
        Assert.Equal([binh], await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// <c>NOTIF-03</c>: ba người khác nhau, cùng nhóm → MỘT dòng, <c>actor_count = 3</c>, <c>last_actor_id</c> là người CUỐI, đủ ba
    /// dòng người. Dòng đầu mang đích và mốc tạo; mỗi sự kiện sau đẩy <c>updated_at</c> lên (cạm bẫy 5), <c>created_at</c> đứng yên.
    /// </summary>
    [Fact]
    public async Task NOTIF_03_ba_nguoi_khac_nhau_mot_dong_dem_ba_nguoi_cuoi()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        Guid[] nguoi = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        Assert.Equal(UpsertResult.Created, await UpsertAsync(CamXuc(an, bai, nguoi[0])));
        _clock.Now = T0.AddMinutes(1);
        Assert.Equal(UpsertResult.Updated, await UpsertAsync(CamXuc(an, bai, nguoi[1])));
        _clock.Now = T0.AddMinutes(2);
        Assert.Equal(UpsertResult.Updated, await UpsertAsync(CamXuc(an, bai, nguoi[2])));

        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(3, dong.ActorCount);
        Assert.Equal(nguoi[2], dong.LastActor);
        Assert.False(dong.IsRead);
        Assert.Equal((NotificationTypes.Reaction, NotificationTargetTypes.Post, bai, (Guid?)bai),
            (dong.Type, dong.TargetType, dong.TargetId, dong.PostId));
        Assert.Null(dong.ReasonCode);
        Assert.Equal((T0, T0.AddMinutes(2)), (dong.CreatedAt, dong.UpdatedAt));
        Assert.Equal(nguoi.Order(), await NguoiCuaAsync(dong.Id));
    }

    /// <summary><c>NOTIF-04</c> (cạm bẫy 2): cùng một người ba lần (thả, gỡ, thả lại) → <c>actor_count</c> vẫn 1 — đếm người, không đếm lượt.</summary>
    [Fact]
    public async Task NOTIF_04_cung_mot_nguoi_ba_lan_van_dem_mot()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        var binh = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            _clock.Now = T0.AddMinutes(i);
            await UpsertAsync(CamXuc(an, bai, binh));
        }

        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(1, dong.ActorCount);
        Assert.Equal(T0.AddMinutes(2), dong.UpdatedAt);   // vẫn nhảy lên đầu — "có gì mới" dù cùng người
        Assert.Equal([binh], await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// <c>NOTIF-05</c> (Đ-6.16 "đợt"): hai người, người nhận đọc nhóm, rồi một người MỚI → nhóm chưa đọc lại, <c>actor_count = 1</c>,
    /// <c>notification_actors</c> chỉ còn người mới. Cùng dòng (không chèn dòng thứ hai).
    /// </summary>
    [Fact]
    public async Task NOTIF_05_doc_roi_co_nguoi_moi_la_dot_moi_dem_lai_tu_mot()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        await UpsertAsync(CamXuc(an, bai, Guid.NewGuid()));
        await UpsertAsync(CamXuc(an, bai, Guid.NewGuid()));
        var truoc = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(2, truoc.ActorCount);
        await DanhDauDaDocAsync(truoc.Id);

        var cuong = Guid.NewGuid();
        Assert.Equal(UpsertResult.Updated, await UpsertAsync(CamXuc(an, bai, cuong)));

        var sau = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(truoc.Id, sau.Id);
        Assert.False(sau.IsRead);
        Assert.Equal((1, (Guid?)cuong), (sau.ActorCount, sau.LastActor));
        Assert.Equal([cuong], await NguoiCuaAsync(sau.Id));
    }

    /// <summary>
    /// <c>NOTIF-05b</c> (đề xuất): đọc rồi NGƯỜI CŨ quay lại → vẫn là đợt mới, <c>actor_count = 1</c>. Bỏ bước xóa người của đợt trước thì
    /// ca này vẫn xanh ở số đếm (người cũ không chèn được → không tăng) nhưng người thứ hai tới sau sẽ không được đếm — vế cuối bắt điều đó.
    /// </summary>
    [Fact]
    public async Task NOTIF_05b_doc_roi_nguoi_cu_quay_lai_van_dot_moi()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        var binh = Guid.NewGuid();
        var cuong = Guid.NewGuid();
        await UpsertAsync(CamXuc(an, bai, binh));
        await UpsertAsync(CamXuc(an, bai, cuong));
        await DanhDauDaDocAsync(Assert.Single(await DongCuaAsync(an)).Id);

        await UpsertAsync(CamXuc(an, bai, binh));
        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal((1, (Guid?)binh, false), (dong.ActorCount, dong.LastActor, dong.IsRead));
        Assert.Equal([binh], await NguoiCuaAsync(dong.Id));

        // Cường thuộc đợt TRƯỚC — trong đợt này Cường là người mới, phải được đếm.
        await UpsertAsync(CamXuc(an, bai, cuong));
        dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(2, dong.ActorCount);
        Assert.Equal(new[] { binh, cuong }.Order(), await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// <c>NOTIF-09-store</c> (Đ-6.17, B.10 #7): thông báo kiểm duyệt không có người — <c>last_actor_id</c> NULL, 0 dòng
    /// <c>notification_actors</c>, <c>actor_count = 1</c>, có <c>reason_code</c>. Ẩn lần hai (khôi phục rồi ẩn lại) trước khi đọc: vẫn một
    /// dòng, đếm 1, lý do là lý do MỚI.
    /// </summary>
    [Fact]
    public async Task NOTIF_09_store_kiem_duyet_khong_nguoi_co_ly_do()
    {
        var tacGia = Guid.NewGuid();
        var bai = Guid.NewGuid();

        Assert.Equal(UpsertResult.Created, await UpsertAsync(KiemDuyet(tacGia, bai, "spam")));
        var dong = Assert.Single(await DongCuaAsync(tacGia));
        Assert.Equal((1, (Guid?)null, "spam", NotificationTypes.Moderation), (dong.ActorCount, dong.LastActor, dong.ReasonCode, dong.Type));
        Assert.Empty(await NguoiCuaAsync(dong.Id));

        _clock.Now = T0.AddMinutes(5);
        Assert.Equal(UpsertResult.Updated, await UpsertAsync(KiemDuyet(tacGia, bai, "violence")));
        dong = Assert.Single(await DongCuaAsync(tacGia));
        Assert.Equal((1, (Guid?)null, "violence", T0.AddMinutes(5)), (dong.ActorCount, dong.LastActor, dong.ReasonCode, dong.UpdatedAt));
        Assert.Empty(await NguoiCuaAsync(dong.Id));
    }

    /// <summary>Hai nhóm khác <c>group_key</c> của cùng một người là hai dòng; cùng <c>group_key</c> của hai người nhận cũng là hai dòng.</summary>
    [Fact]
    public async Task Khoa_gop_la_cap_nguoi_nhan_va_group_key()
    {
        var an = Guid.NewGuid();
        var binh = Guid.NewGuid();
        var bai1 = Guid.NewGuid();
        var bai2 = Guid.NewGuid();
        var cuong = Guid.NewGuid();

        await UpsertAsync(CamXuc(an, bai1, cuong));
        await UpsertAsync(CamXuc(an, bai2, cuong));
        await UpsertAsync(CamXuc(binh, bai1, cuong));

        Assert.Equal(2, (await DongCuaAsync(an)).Count);
        Assert.Single(await DongCuaAsync(binh));
    }

    /// <summary>
    /// Handler viết nhầm phải nổ TRƯỚC mọi I/O: <c>moderation</c> mang người (id Moderator lọt vào <c>last_actor_id</c>), loại khác thiếu
    /// người, mã lý do trên loại khác <c>moderation</c>. Không dòng nào được ghi.
    /// </summary>
    [Fact]
    public async Task Sai_cap_loai_va_nguoi_nem_truoc_khi_ghi()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => UpsertAsync(KiemDuyet(an, bai, "spam") with { ActorId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => UpsertAsync(CamXuc(an, bai, Guid.NewGuid()) with { ActorId = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => UpsertAsync(CamXuc(an, bai, Guid.NewGuid()) with { ReasonCode = "spam" }));

        Assert.Empty(await DongCuaAsync(an));
    }

    // ---------------------------------------------------------------- đồng thời (Mục 10.2 — chạy 20 lần liền trước khi tin)

    /// <summary>
    /// <c>NOTIF-C1</c> ⭐: 20 người khác nhau song song vào nhóm CHƯA CÓ → một dòng, <c>actor_count = 20</c>, 20 dòng người, không lượt
    /// nào ném. Một lượt <c>Created</c>, mười chín lượt <c>Updated</c>. Ca này để lịch chạy tự quyết nên KHÔNG bảo đảm có lượt đi qua nhánh
    /// "người khác vừa chèn" — đo khi thi công: bỏ bước chạy lại sau <c>DO NOTHING</c> (cạm bẫy 3) ca này vẫn xanh. Lưới của nhánh đó là
    /// <see cref="NOTIF_C1b_luot_thua_chen_chay_lai_buoc_mot_va_duoc_dem"/>.
    /// </summary>
    [Fact]
    public async Task NOTIF_C1_hai_muoi_nguoi_song_song_vao_nhom_chua_co()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        var nguoi = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        var ketQua = await Task.WhenAll(nguoi.Select(n => Task.Run(() => UpsertAsync(CamXuc(an, bai, n)))));

        Assert.Single(ketQua, r => r == UpsertResult.Created);
        Assert.Equal(19, ketQua.Count(r => r == UpsertResult.Updated));
        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(20, dong.ActorCount);
        Assert.Contains(dong.LastActor!.Value, nguoi);
        Assert.Equal(nguoi.Order(), await NguoiCuaAsync(dong.Id));
    }

    /// <summary><c>NOTIF-C2</c> (đề xuất): cùng một người 20 lượt song song → <c>actor_count = 1</c>.</summary>
    [Fact]
    public async Task NOTIF_C2_mot_nguoi_hai_muoi_luot_song_song_dem_mot()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        var binh = Guid.NewGuid();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => UpsertAsync(CamXuc(an, bai, binh)))));

        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(1, dong.ActorCount);
        Assert.Equal([binh], await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// <c>NOTIF-C3</c> (thêm khi thi công): 20 người mới song song vào nhóm ĐÃ ĐỌC → đúng MỘT lượt mở đợt mới (xóa người cũ, đếm lại 1),
    /// mười chín lượt sau đếm tiếp trong đợt đó: <c>actor_count = 20</c>, chỉ 20 người mới. Cùng giới hạn như <c>NOTIF-C1</c>: lượt đua
    /// không bảo đảm — lưới của "đọc <c>is_read</c> dưới khóa" là <see cref="NOTIF_C3b_doc_is_read_duoi_khoa_mot_luot_mo_dot_moi"/>.
    /// </summary>
    [Fact]
    public async Task NOTIF_C3_hai_muoi_nguoi_song_song_vao_nhom_da_doc()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        var cu = Guid.NewGuid();
        await UpsertAsync(CamXuc(an, bai, cu));
        await DanhDauDaDocAsync(Assert.Single(await DongCuaAsync(an)).Id);
        var nguoi = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(nguoi.Select(n => Task.Run(() => UpsertAsync(CamXuc(an, bai, n)))));

        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.False(dong.IsRead);
        Assert.Equal(20, dong.ActorCount);
        Assert.Equal(nguoi.Order(), await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// <c>NOTIF-C1b</c> (thêm khi thi công): ÉP nhánh "người khác vừa chèn". <c>NOTIF-C1</c> để lịch chạy tự quyết — lượt đầu thường
    /// <c>COMMIT</c> xong trước khi lượt sau tới bước 1, nên nhánh chạy lại sau <c>DO NOTHING</c> có thể không lượt nào đi qua (đo khi thi
    /// công: bỏ hẳn bước chạy lại, <c>NOTIF-C1</c> vẫn xanh). Ở đây test tự chèn nhóm trong một transaction CHƯA commit: năm lượt upsert
    /// không thấy dòng ở bước 1, cùng chờ ở index unique; test commit → cả năm <c>DO NOTHING</c> → phải chạy lại bước 1 và đếm được mình.
    /// </summary>
    [Fact]
    public async Task NOTIF_C1b_luot_thua_chen_chay_lai_buoc_mot_va_duoc_dem()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        var nguoiDau = Guid.NewGuid();
        var nguoi = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var groupKey = GroupKey.Reaction(ReactionTargetKind.Post, bai);

        await using var giu = new NpgsqlConnection(_cs);
        await giu.OpenAsync();
        await using var tx = await giu.BeginTransactionAsync();
        await using (var chen = new NpgsqlCommand(
            "with n as (insert into notification.notifications " +
            "(id, recipient_id, type, group_key, target_type, target_id, post_id, last_actor_id) " +
            "values ($1, $2, 'reaction', $3, 'post', $4, $4, $5) returning id) " +
            "insert into notification.notification_actors (notification_id, actor_id) select id, $5 from n", giu, tx))
        {
            chen.Parameters.AddWithValue(Guid.NewGuid());
            chen.Parameters.AddWithValue(an);
            chen.Parameters.AddWithValue(groupKey);
            chen.Parameters.AddWithValue(bai);
            chen.Parameters.AddWithValue(nguoiDau);
            await chen.ExecuteNonQueryAsync();
        }

        var luot = nguoi.Select(n => Task.Run(() => UpsertAsync(CamXuc(an, bai, n)))).ToArray();
        await ChoDangChoKhoaAsync(nguoi.Length);
        await tx.CommitAsync();
        var ketQua = await Task.WhenAll(luot);

        Assert.All(ketQua, r => Assert.Equal(UpsertResult.Updated, r));
        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.Equal(1 + nguoi.Length, dong.ActorCount);
        Assert.Equal(nguoi.Append(nguoiDau).Order(), await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// <c>NOTIF-C3b</c> (thêm khi thi công): ÉP lượt đua trên nhóm ĐÃ ĐỌC. Test khóa sẵn dòng (<c>FOR UPDATE</c>) rồi cho năm lượt upsert
    /// chạy: đọc <c>is_read</c> cũ KHÔNG dưới khóa thì cả năm cùng thấy "đã đọc", cùng mở đợt mới, xóa người của nhau — còn một người,
    /// đếm 1. Đọc dưới khóa thì chỉ lượt đầu thấy "đã đọc".
    /// </summary>
    [Fact]
    public async Task NOTIF_C3b_doc_is_read_duoi_khoa_mot_luot_mo_dot_moi()
    {
        var an = Guid.NewGuid();
        var bai = Guid.NewGuid();
        await UpsertAsync(CamXuc(an, bai, Guid.NewGuid()));
        var id = Assert.Single(await DongCuaAsync(an)).Id;
        await DanhDauDaDocAsync(id);
        var nguoi = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        await using var giu = new NpgsqlConnection(_cs);
        await giu.OpenAsync();
        await using var tx = await giu.BeginTransactionAsync();
        await using (var khoa = new NpgsqlCommand("select 1 from notification.notifications where id = $1 for update", giu, tx))
        {
            khoa.Parameters.AddWithValue(id);
            await khoa.ExecuteNonQueryAsync();
        }

        var luot = nguoi.Select(n => Task.Run(() => UpsertAsync(CamXuc(an, bai, n)))).ToArray();
        await ChoDangChoKhoaAsync(nguoi.Length);
        await tx.CommitAsync();
        await Task.WhenAll(luot);

        var dong = Assert.Single(await DongCuaAsync(an));
        Assert.False(dong.IsRead);
        Assert.Equal(nguoi.Length, dong.ActorCount);
        Assert.Equal(nguoi.Order(), await NguoiCuaAsync(dong.Id));
    }

    /// <summary>
    /// Chờ tới khi đúng <paramref name="soLuot"/> kết nối của database này đứng chờ khóa — mọi lượt upsert đã tới chỗ chặn, commit lúc
    /// này mới là lượt đua thật. Hỏi Postgres, không đoán bằng thời gian ngủ.
    /// </summary>
    private async Task ChoDangChoKhoaAsync(int soLuot)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        var hanChot = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await using var cmd = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'", conn);
            var dangCho = (long)(await cmd.ExecuteScalarAsync())!;
            if (dangCho == soLuot)
                return;
            Assert.True(DateTime.UtcNow < hanChot, $"Sau 15 giây chỉ {dangCho}/{soLuot} lượt đứng chờ khóa.");
            await Task.Delay(20);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
