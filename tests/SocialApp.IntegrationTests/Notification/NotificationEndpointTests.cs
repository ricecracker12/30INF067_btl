using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.IntegrationTests.Notification;

/// <summary>
/// GĐ6 D11 — bốn endpoint của <c>notification-v1</c> (Mục 8.3) trên Postgres thật. Thông báo dựng bằng <see cref="INotificationStore"/>
/// (D9 — đã có test gộp riêng) cho tất định: lời mời thật đi qua bus bất đồng bộ, và đường event → thông báo đã có
/// <c>NotificationHandlerTests</c>. Mọi khẳng định đọc qua API, trừ <c>updated_at</c>/<c>is_read</c> của <c>NOTIF-10</c> (soi DB để chắc
/// không cột nào bị chạm).
///
/// Không Redis: không endpoint nào của nhóm này đặc quyền (fail-open như mọi endpoint thường).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NotificationEndpointTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------- dựng cảnh

    private async Task UpsertAsync(NotificationUpsert upsert)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationStore>().UpsertAsync(upsert, default);
    }

    /// <summary>Cảm xúc của <paramref name="actor"/> trên bài <paramref name="post"/> của <paramref name="recipient"/>.</summary>
    private Task CamXucAsync(Guid recipient, Guid post, Guid actor) => UpsertAsync(new NotificationUpsert(
        recipient, NotificationTypes.Reaction, GroupKey.Reaction(ReactionTargetKind.Post, post),
        NotificationTargetTypes.Post, post, post, actor, null));

    private static async Task<HttpResponseMessage> GuiAsync(
        ModulesTestClient client, HttpMethod method, string path, Guid? caller, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (caller is { } id)
            request.Headers.Authorization = ModulesTestClient.Bearer(id);
        return await client.Http.SendAsync(request);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> DanhSachAsync(ModulesTestClient client, Guid me, string query = "")
    {
        using var response = await GuiAsync(client, HttpMethod.Get, "/api/v1/notifications" + query, me);
        return await OkAsync(response);
    }

    private static async Task<List<Guid>> IdsAsync(ModulesTestClient client, Guid me) =>
        [.. (await DanhSachAsync(client, me)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("notificationId").GetGuid())];

    private static async Task<int> ChuaDocAsync(ModulesTestClient client, Guid me)
    {
        using var response = await GuiAsync(client, HttpMethod.Get, "/api/v1/notifications/unread-count", me);
        return (await OkAsync(response)).GetProperty("total").GetInt32();
    }

    private static async Task DanhDauAsync(ModulesTestClient client, Guid me, Guid id)
    {
        using var response = await GuiAsync(client, HttpMethod.Post, $"/api/v1/notifications/{id}/read", me);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---------------------------------------------------------------- Mục 10.1

    /// <summary><c>NOTIF-06</c>: hai nhóm chưa đọc gồm bảy sự kiện, cộng một nhóm đã đọc → <c>unread-count = 2</c> — đếm NHÓM, không đếm sự kiện.</summary>
    [Fact]
    public async Task NOTIF_06_unread_count_dem_nhom_khong_dem_su_kien()
    {
        var client = new ModulesTestClient(factory);
        var an = Guid.NewGuid();
        var bai1 = Guid.NewGuid();
        var bai2 = Guid.NewGuid();
        for (var i = 0; i < 4; i++)
            await CamXucAsync(an, bai1, Guid.NewGuid());
        for (var i = 0; i < 3; i++)
            await CamXucAsync(an, bai2, Guid.NewGuid());
        await CamXucAsync(an, Guid.NewGuid(), Guid.NewGuid());
        await DanhDauAsync(client, an, (await IdsAsync(client, an))[0]);   // nhóm mới nhất — bài thứ ba

        Assert.Equal(2, await ChuaDocAsync(client, an));
        Assert.Equal(0, await ChuaDocAsync(client, Guid.NewGuid()));   // người không có thông báo nào
    }

    /// <summary>
    /// <c>NOTIF-07</c> (Mục 8.3, cạm bẫy 3): hai nhóm G1 (cũ) và G2 (tới SAU lúc mở chuông); <c>read-all { upTo = G1.updatedAt }</c> → G1 đã
    /// đọc, G2 vẫn chưa. Rồi một sự kiện mới vào G1 → G1 chưa đọc lại. Thông báo của người khác không bị chạm.
    /// </summary>
    [Fact]
    public async Task NOTIF_07_read_all_chi_toi_upTo_khong_nuot_thong_bao_toi_sau()
    {
        var client = new ModulesTestClient(factory);
        var an = Guid.NewGuid();
        var binh = Guid.NewGuid();
        var bai1 = Guid.NewGuid();
        var bai2 = Guid.NewGuid();
        await CamXucAsync(an, bai1, Guid.NewGuid());
        var luocMoChuong = (await DanhSachAsync(client, an)).GetProperty("items")[0].GetProperty("updatedAt").GetString();
        await CamXucAsync(an, bai2, Guid.NewGuid());
        await CamXucAsync(binh, bai1, Guid.NewGuid());

        using (var response = await GuiAsync(client, HttpMethod.Post, "/api/v1/notifications/read-all", an, new { upTo = luocMoChuong }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var items = (await DanhSachAsync(client, an)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [(bai2, false), (bai1, true)],
            items.Select(i => (i.GetProperty("target").GetProperty("id").GetGuid(), i.GetProperty("isRead").GetBoolean())));
        Assert.Equal(1, await ChuaDocAsync(client, binh));

        await CamXucAsync(an, bai1, Guid.NewGuid());
        Assert.Equal(2, await ChuaDocAsync(client, an));
    }

    /// <summary>
    /// <c>NOTIF-08</c> (nếp <c>FEED-Q1</c>): số câu SQL của <c>GET /notifications</c> bằng nhau giữa trang một nhóm và trang hai mươi nhóm,
    /// mỗi nhóm một người KHÁC NHAU có hồ sơ — hydrate tên một lô, không mỗi dòng một câu.
    /// </summary>
    [Fact]
    public async Task NOTIF_08_so_cau_SQL_khong_doi_giua_1_va_20_nhom()
    {
        var client = new ModulesTestClient(factory);
        var it = Guid.NewGuid();
        var nhieu = Guid.NewGuid();
        var nguoi = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var id = Guid.NewGuid();
            await client.PutProfileOkAsync(id, new { displayName = $"Người thả tim {i}" });
            nguoi.Add(id);
            await CamXucAsync(nhieu, Guid.NewGuid(), id);
        }

        await CamXucAsync(it, Guid.NewGuid(), nguoi[0]);

        using var counter = new SqlCommandCounter(factory.ConnectionString);
        var motNhom = await DanhSachAsync(client, it);
        var cauMotNhom = counter.Statements.Count;
        counter.Reset();
        var haiMuoiNhom = await DanhSachAsync(client, nhieu);
        var cauHaiMuoiNhom = counter.Statements.Count;

        Assert.Single(motNhom.GetProperty("items").EnumerateArray());
        Assert.Equal(20, haiMuoiNhom.GetProperty("items").GetArrayLength());
        Assert.All(haiMuoiNhom.GetProperty("items").EnumerateArray(),
            i => Assert.StartsWith("Người thả tim ", i.GetProperty("actor").GetProperty("displayName").GetString()));
        Assert.Equal(cauMotNhom, cauHaiMuoiNhom);
    }

    /// <summary>
    /// <c>NOTIF-10</c> (đề xuất, cạm bẫy 1): ba nhóm; đánh dấu nhóm CŨ NHẤT đã đọc → thứ tự danh sách không đổi, <c>updatedAt</c> của nó
    /// không đổi. Đánh dấu lần hai vẫn 204 (idempotent).
    /// </summary>
    [Fact]
    public async Task NOTIF_10_danh_dau_da_doc_khong_doi_thu_tu()
    {
        var client = new ModulesTestClient(factory);
        var an = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            await CamXucAsync(an, Guid.NewGuid(), Guid.NewGuid());
        var truoc = (await DanhSachAsync(client, an)).GetProperty("items").EnumerateArray().ToList();
        var cuNhat = truoc[^1];
        var id = cuNhat.GetProperty("notificationId").GetGuid();

        await DanhDauAsync(client, an, id);
        await DanhDauAsync(client, an, id);

        var sau = (await DanhSachAsync(client, an)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            truoc.Select(i => i.GetProperty("notificationId").GetGuid()),
            sau.Select(i => i.GetProperty("notificationId").GetGuid()));
        Assert.True(sau[^1].GetProperty("isRead").GetBoolean());
        Assert.Equal(cuNhat.GetProperty("updatedAt").GetString(), sau[^1].GetProperty("updatedAt").GetString());
        Assert.Equal(2, await ChuaDocAsync(client, an));
    }

    /// <summary>
    /// <c>NOTIF-IDOR</c> (quy ước 3b): A đánh dấu thông báo của B → 403; id không tồn tại → 403 — CÙNG thân lỗi (so mọi trường trừ
    /// <c>traceId</c>, <c>instance</c>). Thông báo của B vẫn chưa đọc.
    /// </summary>
    [Fact]
    public async Task NOTIF_IDOR_cua_nguoi_khac_va_khong_ton_tai_cung_403_cung_than()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await CamXucAsync(b, Guid.NewGuid(), Guid.NewGuid());
        var cuaB = (await IdsAsync(client, b))[0];

        using var nguoiKhac = await GuiAsync(client, HttpMethod.Post, $"/api/v1/notifications/{cuaB}/read", a);
        using var khongTonTai = await GuiAsync(client, HttpMethod.Post, $"/api/v1/notifications/{Guid.NewGuid()}/read", a);

        Assert.Equal(HttpStatusCode.Forbidden, nguoiKhac.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, khongTonTai.StatusCode);
        Assert.Equal(await ThanLoiAsync(nguoiKhac), await ThanLoiAsync(khongTonTai));
        Assert.Equal(1, await ChuaDocAsync(client, b));

        static async Task<string> ThanLoiAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return string.Join("|", document.RootElement.EnumerateObject()
                .Where(p => p.Name is not ("traceId" or "instance"))
                .Select(p => $"{p.Name}={p.Value}"));
        }
    }

    // ---------------------------------------------------------------- thêm khi thi công

    /// <summary>
    /// Hình dạng <c>NotificationResponse</c>: người có hồ sơ + ảnh → <c>actor</c> đủ, <c>avatarUrl</c> ký sẵn; người không hồ sơ → <c>actor</c>
    /// null nhưng <c>actorCount</c> vẫn đếm; <c>moderation</c> → <c>actor</c> null, có <c>reasonCode</c>; đích <c>user</c> → <c>postId</c> null.
    /// Người gọi chỉ thấy thông báo của mình.
    /// </summary>
    [Fact]
    public async Task Hinh_dang_thong_bao_va_chi_thay_cua_minh()
    {
        var client = new ModulesTestClient(factory);
        var an = Guid.NewGuid();
        var binh = Guid.NewGuid();
        await client.PutProfileOkAsync(binh, new { displayName = "Bình" });
        using (var avatar = await client.SetAvatarAsync(binh, client.PutAvatarObject(binh)))
            Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        var baiAn = Guid.NewGuid();
        var baiThuong = Guid.NewGuid();

        await UpsertAsync(new NotificationUpsert(an, NotificationTypes.FriendRequest, GroupKey.FriendRequest(binh),
            NotificationTargetTypes.User, binh, null, binh, null));
        await CamXucAsync(an, baiThuong, Guid.NewGuid());   // người không có hồ sơ
        await UpsertAsync(new NotificationUpsert(an, NotificationTypes.Moderation, GroupKey.Moderation(ModerationTargetType.Post, baiAn),
            NotificationTargetTypes.Post, baiAn, baiAn, null, "spam"));
        await CamXucAsync(Guid.NewGuid(), baiThuong, binh);   // của người khác

        var items = (await DanhSachAsync(client, an)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["moderation", "reaction", "friend_request"], items.Select(i => i.GetProperty("type").GetString()));

        var kiemDuyet = items[0];
        Assert.Equal(JsonValueKind.Null, kiemDuyet.GetProperty("actor").ValueKind);
        Assert.Equal("spam", kiemDuyet.GetProperty("reasonCode").GetString());
        Assert.Equal(1, kiemDuyet.GetProperty("actorCount").GetInt32());
        Assert.Equal(("post", baiAn, baiAn), (
            kiemDuyet.GetProperty("target").GetProperty("type").GetString(),
            kiemDuyet.GetProperty("target").GetProperty("id").GetGuid(),
            kiemDuyet.GetProperty("target").GetProperty("postId").GetGuid()));

        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("actor").ValueKind);
        Assert.Equal(1, items[1].GetProperty("actorCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("reasonCode").ValueKind);

        var loiMoi = items[2];
        var actor = loiMoi.GetProperty("actor");
        Assert.Equal((binh, "Bình"), (actor.GetProperty("userId").GetGuid(), actor.GetProperty("displayName").GetString()));
        Assert.StartsWith("https://", actor.GetProperty("avatarUrl").GetString());
        Assert.Equal(("user", binh, JsonValueKind.Null), (
            loiMoi.GetProperty("target").GetProperty("type").GetString(),
            loiMoi.GetProperty("target").GetProperty("id").GetGuid(),
            loiMoi.GetProperty("target").GetProperty("postId").ValueKind));
        Assert.False(loiMoi.GetProperty("isRead").GetBoolean());
    }

    /// <summary>25 nhóm, <c>limit = 10</c> → 10/10/5 theo <c>updatedAt</c> giảm dần, không trùng không sót, trang cuối <c>nextCursor = null</c>.</summary>
    [Fact]
    public async Task Phan_trang_keyset_khong_trung_khong_sot()
    {
        var client = new ModulesTestClient(factory);
        var an = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
            await CamXucAsync(an, Guid.NewGuid(), Guid.NewGuid());

        var thay = new List<Guid>();
        var moc = new List<DateTimeOffset>();
        var soDong = new List<int>();
        string? cursor = null;
        do
        {
            var page = await DanhSachAsync(client, an, "?limit=10" + (cursor is null ? "" : $"&cursor={cursor}"));
            var items = page.GetProperty("items").EnumerateArray().ToList();
            soDong.Add(items.Count);
            thay.AddRange(items.Select(i => i.GetProperty("notificationId").GetGuid()));
            moc.AddRange(items.Select(i => i.GetProperty("updatedAt").GetDateTimeOffset()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal([10, 10, 5], soDong);
        Assert.Equal(25, thay.Distinct().Count());
        Assert.Equal(moc.OrderByDescending(m => m), moc);
    }

    public static TheoryData<string, string, string> ThamSoSai => new()
    {
        { "GET", "/api/v1/notifications?limit=0", "limit" },
        { "GET", "/api/v1/notifications?limit=51", "limit" },
        { "GET", "/api/v1/notifications?cursor=rac", "cursor" },
        { "POST", "/api/v1/notifications/khong-phai-uuid/read", "notificationId" },
        { "POST", "/api/v1/notifications/read-all", "upTo" },
    };

    /// <summary>400 theo đúng trường. <c>read-all</c> gửi body rỗng <c>{}</c> — thiếu <c>upTo</c> thì không được coi là "đọc hết".</summary>
    [Theory]
    [MemberData(nameof(ThamSoSai))]
    public async Task Tham_so_sai_400_dung_truong(string method, string path, string field)
    {
        var client = new ModulesTestClient(factory);
        using var response = await GuiAsync(client, new HttpMethod(method), path, Guid.NewGuid(), method == "POST" && path.EndsWith("read-all") ? new { } : null);

        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Contains(field, errors.Keys);
    }
}
