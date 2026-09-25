using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// GĐ6 D8 — <c>GET /admin/audit-logs</c> (Đ-6.15, ENT-13) trên Postgres + Redis thật (endpoint đặc quyền, fail-closed).
///
/// Dòng nhật ký dựng bằng <c>INSERT</c> thẳng (bảng append-only cho INSERT): 120 dòng trộn ba người, bốn hành động, ba loại đối tượng —
/// đi qua API thật để có 120 dòng là 120 lần khóa/đổi vai trò/ẩn bài, chậm mà không chứng minh thêm gì cho ĐƯỜNG ĐỌC. Mỗi ca tự dựng
/// người và đối tượng MỚI, nên các ca chung database lọc được phần của mình; ca không lọc khẳng định "chứa đủ", không "đúng bằng".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLogQueryTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly string[] Actions = ["report.hide", "report.dismiss", "user.lock", "role.assign"];

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

    private sealed record Dong(long Id, Guid Actor, string Action, string? TargetType, Guid? TargetId);

    private sealed record Canh(Guid A, Guid B, Guid C, Guid Bai, Guid NguoiDung, List<Dong> Dongs);

    /// <summary>
    /// 120 dòng: người A (có hồ sơ), B, C (không hồ sơ) xoay vòng theo <c>i % 3</c>; bốn hành động theo <c>i % 4</c>; đối tượng theo
    /// <c>i / 3 % 3</c> — bài · người dùng · vai trò (không id). Nhịp khác nhau để mọi người có dòng trên mọi đối tượng. Trả theo thứ tự
    /// chèn (id tăng).
    /// </summary>
    private async Task<Canh> DungCanhAsync(ModulesTestClient client)
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await client.PutProfileOkAsync(a, new { displayName = "Admin A" });
        var (bai, nguoiDung) = (Guid.NewGuid(), Guid.NewGuid());
        var actors = new[] { a, b, c };
        var dongs = new List<Dong>();

        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 120; i++)
        {
            var (type, target) = (i / 3 % 3) switch { 0 => ("post", (Guid?)bai), 1 => ("user", nguoiDung), _ => ("role", (Guid?)null) };
            await using var cmd = new NpgsqlCommand(
                "insert into moderation.audit_logs (actor_id, action, target_type, target_id, metadata, ip, created_at) " +
                "values ($1, $2, $3, $4, $5::jsonb, $6::inet, now()) returning id", conn);
            cmd.Parameters.AddWithValue(actors[i % 3]);
            cmd.Parameters.AddWithValue(Actions[i % 4]);
            cmd.Parameters.AddWithValue(type);
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)target ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
            cmd.Parameters.AddWithValue($$"""{"thuTu": {{i}}}""");
            cmd.Parameters.AddWithValue($"198.51.100.{i % 250 + 1}");
            var id = (long)(await cmd.ExecuteScalarAsync())!;
            dongs.Add(new Dong(id, actors[i % 3], Actions[i % 4], type, target));
        }

        return new Canh(a, b, c, bai, nguoiDung, dongs);
    }

    private static async Task<HttpResponseMessage> GetAsync(ModulesTestClient client, string query, string role = "ADMIN")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/audit-logs" + query);
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), role);
        return await client.Http.SendAsync(request);
    }

    /// <summary>Đi hết các trang; trả id từng trang theo thứ tự nhận.</summary>
    private static async Task<List<List<long>>> CacTrangAsync(ModulesTestClient client, string filter, int limit)
    {
        var trang = new List<List<long>>();
        string? cursor = null;
        do
        {
            var query = $"?limit={limit}{filter}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var response = await GetAsync(client, query);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{query} → {(int)response.StatusCode}: {body}");
            var root = JsonDocument.Parse(body).RootElement;
            trang.Add([.. root.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64())]);
            cursor = root.GetProperty("nextCursor").GetString();
        } while (cursor is not null && trang.Count < 50);

        Assert.Null(cursor);
        return trang;
    }

    private static List<long> Giam(IEnumerable<Dong> dongs) => [.. dongs.Select(d => d.Id).OrderDescending()];

    /// <summary><c>AUD-04</c> — không lọc: <c>id</c> giảm NGHIÊM NGẶT qua mọi trang (không trùng), và chứa đủ 120 dòng của ca.</summary>
    [Fact]
    public async Task AUD_04_khong_loc_id_giam_dan_khong_trung_khong_sot()
    {
        var client = new ModulesTestClient(factory);
        var canh = await DungCanhAsync(client);

        var tatCa = (await CacTrangAsync(client, "", 50)).SelectMany(t => t).ToList();

        Assert.True(tatCa.Zip(tatCa.Skip(1)).All(p => p.First > p.Second), "id phải giảm nghiêm ngặt qua các trang");
        Assert.Subset(tatCa.ToHashSet(), canh.Dongs.Select(d => d.Id).ToHashSet());
    }

    /// <summary><c>AUD-04</c> — lọc theo người: 40 dòng của A, <c>limit=15</c> → 15/15/10, đúng thứ tự, không trùng không sót.</summary>
    [Fact]
    public async Task AUD_04_loc_theo_nguoi_ba_trang_15_15_10()
    {
        var client = new ModulesTestClient(factory);
        var canh = await DungCanhAsync(client);

        var trang = await CacTrangAsync(client, $"&actorId={canh.A}", 15);

        Assert.Equal([15, 15, 10], trang.Select(t => t.Count));
        Assert.Equal(Giam(canh.Dongs.Where(d => d.Actor == canh.A)), trang.SelectMany(t => t));
    }

    /// <summary>
    /// <c>AUD-04</c> — lọc theo đối tượng (<c>targetType</c> + <c>targetId</c>), theo loại một mình, theo hành động một mình (L-D14), và
    /// cộng dồn người + hành động.
    /// </summary>
    [Fact]
    public async Task AUD_04_loc_theo_doi_tuong_loai_hanh_dong_va_cong_don()
    {
        var client = new ModulesTestClient(factory);
        var canh = await DungCanhAsync(client);

        Assert.Equal(Giam(canh.Dongs.Where(d => d.TargetId == canh.Bai)),
            (await CacTrangAsync(client, $"&targetType=post&targetId={canh.Bai}", 100)).SelectMany(t => t));
        Assert.Equal(Giam(canh.Dongs.Where(d => d.TargetId == canh.NguoiDung)),
            (await CacTrangAsync(client, $"&targetType=user&targetId={canh.NguoiDung}", 100)).SelectMany(t => t));
        Assert.Equal(Giam(canh.Dongs.Where(d => d.Actor == canh.B && d.Action == "user.lock")),
            (await CacTrangAsync(client, $"&actorId={canh.B}&action=user.lock", 100)).SelectMany(t => t));

        // Hai bộ lọc không có id của ca: kết quả CHỨA đủ phần của ca, và mọi dòng đúng bộ lọc (ca khác chung database).
        var userLock = (await CacTrangAsync(client, "&action=user.lock", 100)).SelectMany(t => t).ToHashSet();
        Assert.Subset(userLock, canh.Dongs.Where(d => d.Action == "user.lock").Select(d => d.Id).ToHashSet());
        Assert.DoesNotContain(canh.Dongs.Where(d => d.Action != "user.lock").Select(d => d.Id), userLock.Contains);

        var role = (await CacTrangAsync(client, "&targetType=role", 100)).SelectMany(t => t).ToHashSet();
        Assert.Subset(role, canh.Dongs.Where(d => d.TargetType == "role").Select(d => d.Id).ToHashSet());
        Assert.DoesNotContain(canh.Dongs.Where(d => d.TargetType != "role").Select(d => d.Id), role.Contains);
    }

    /// <summary>
    /// Hình dạng một dòng: <c>actor</c> hydrate cho người có hồ sơ, <c>null</c> cho người không có; <c>metadata</c> là object JSON nguyên;
    /// <c>ip</c> là chuỗi. Số câu SQL của trang 3 dòng và trang 60 dòng BẰNG nhau — tên người thao tác hydrate một lô.
    /// </summary>
    [Fact]
    public async Task Hinh_dang_dong_va_actor_hydrate_mot_lo()
    {
        var client = new ModulesTestClient(factory);
        var canh = await DungCanhAsync(client);
        using (var warm = await GetAsync(client, "?limit=1"))
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);

        using var counter = new SqlCommandCounter(factory.ConnectionString);
        using var nho = await GetAsync(client, $"?limit=3&targetType=post&targetId={canh.Bai}");
        var soCauNho = counter.Statements.Count;
        counter.Reset();
        using var lon = await GetAsync(client, $"?limit=60&targetType=post&targetId={canh.Bai}");
        var soCauLon = counter.Statements.Count;

        Assert.Equal(soCauNho, soCauLon);
        var items = JsonDocument.Parse(await lon.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(canh.Dongs.Count(d => d.TargetId == canh.Bai), items.Count);

        var cuaA = items.First(i => i.GetProperty("actorId").GetGuid() == canh.A);
        Assert.Equal("Admin A", cuaA.GetProperty("actor").GetProperty("displayName").GetString());
        var cuaB = items.First(i => i.GetProperty("actorId").GetGuid() == canh.B);
        Assert.Equal(JsonValueKind.Null, cuaB.GetProperty("actor").ValueKind);

        Assert.Equal(JsonValueKind.Object, cuaA.GetProperty("metadata").ValueKind);
        Assert.True(cuaA.GetProperty("metadata").TryGetProperty("thuTu", out _));
        Assert.StartsWith("198.51.100.", cuaA.GetProperty("ip").GetString());
        Assert.Equal("post", cuaA.GetProperty("targetType").GetString());
        Assert.Equal(canh.Bai, cuaA.GetProperty("targetId").GetGuid());
    }

    /// <summary><c>AUD-04b</c> <i>(đề xuất)</i> — tham số sai: 400 dưới đúng trường, không 500.</summary>
    [Theory]
    [InlineData("?targetId=0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20", "targetId")]
    [InlineData("?action=abc", "action")]
    [InlineData("?action=USER.LOCK", "action")]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?cursor=rac", "cursor")]
    [InlineData("?actorId=abc", "actorId")]
    public async Task AUD_04b_tham_so_sai_400_dung_truong(string query, string field)
    {
        var client = new ModulesTestClient(factory);
        using var response = await GetAsync(client, query);
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.True(errors.ContainsKey(field), $"Thiếu errors.{field}: {string.Join(", ", errors.Keys)}");
    }

    /// <summary>
    /// <c>audit.read</c> chỉ ADMIN: MODERATOR và USER → 403 (bản endpoint thật của <c>TC-A05-mod-audit</c>), và lần bị từ chối đó CHÍNH
    /// NÓ vào nhật ký (<c>access.denied</c>, route template) — đọc lại được bằng ADMIN.
    /// </summary>
    [Theory]
    [InlineData("MODERATOR")]
    [InlineData("USER")]
    public async Task Khong_phai_admin_403_va_lan_tu_choi_vao_nhat_ky(string role)
    {
        var client = new ModulesTestClient(factory);
        var nguoiGoi = Guid.NewGuid();
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/audit-logs"))
        {
            request.Headers.Authorization = ModulesTestClient.Bearer(nguoiGoi, role);
            using var response = await client.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var doc = await GetAsync(client, $"?actorId={nguoiGoi}&action=access.denied");
        var item = Assert.Single(JsonDocument.Parse(await doc.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("api/v1/admin/audit-logs", item.GetProperty("metadata").GetProperty("routeTemplate").GetString());
    }
}
