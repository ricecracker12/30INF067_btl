using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Admin;

/// <summary>
/// GĐ6 D2 — <c>GET /admin/users</c>, <c>GET /admin/users/{userId}</c> (UC-20, <c>admin-v1.yaml</c>) trên Postgres + Redis thật.
///
/// Redis THẬT là bắt buộc, không phải cho đủ bộ: endpoint mang <c>[PrivilegedEndpoint]</c> fail-closed (Đ-6.8), nên với Redis cổng 1
/// mặc định của <see cref="ModulesApiFactory"/> mọi request có token đều 503 trước khi tới tầng 2 (<c>FailClosedTests</c>).
///
/// Tài khoản dựng bằng SQL (<see cref="IdentitySql"/>, B1); người gọi ký token bằng <c>TestJwt</c> — tầng 2 chỉ đọc claim
/// <c>role</c>, không cần Admin gọi có dòng trong <c>users</c>. Mỗi ca dùng một tiền tố email riêng nên các ca chung database không
/// thấy tài khoản của nhau qua <c>q</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminUsersTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly Guid Admin = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);

        // Kết nối Redis của APP mở ở nền lúc host khởi động — chưa xong thì kiểm thu hồi ra Unknown và endpoint đặc quyền 503.
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    /// <summary>Trả kết nối của database riêng về Postgres (cạm bẫy 7 Mục 3 của hướng dẫn khối A+C) — lớp này mở thêm một pool.</summary>
    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    private static string NewPrefix(string tag) => $"{tag}{Guid.NewGuid():N}"[..(tag.Length + 10)];

    private async Task<HttpResponseMessage> GetAsync(string path, Guid? caller = null, string role = "ADMIN")
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = ModulesTestClient.Bearer(caller ?? Admin, role);
        return await client.SendAsync(request);
    }

    private async Task<JsonElement> GetOkAsync(string path)
    {
        using var response = await GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} → {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string ListPath(string? q = null, int? limit = null, string? cursor = null, string? extra = null)
    {
        var parts = new List<string>();
        if (q is not null) parts.Add($"q={Uri.EscapeDataString(q)}");
        if (limit is not null) parts.Add($"limit={limit}");
        if (cursor is not null) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (extra is not null) parts.Add(extra);
        return "/api/v1/admin/users" + (parts.Count > 0 ? "?" + string.Join('&', parts) : "");
    }

    private static List<Guid> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userId").GetGuid())];

    private static List<string> Emails(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("email").GetString()!)];

    /// <summary>
    /// ADM-07: 45 tài khoản khớp <c>q</c>, <c>limit=20</c> → 20 / 20 / 5, không trùng không sót, đúng thứ tự
    /// <c>(created_at DESC, user_id DESC)</c>, <c>nextCursor</c> null ở trang cuối. Ba nhóm 15 tài khoản CHUNG một <c>created_at</c>
    /// — ranh giới trang 1/2 và 2/3 rơi giữa nhóm, nên thiếu khóa phụ <c>user_id</c> là trùng/sót (cạm bẫy 4). Ba tài khoản ngoài
    /// tiền tố không được lọt vào.
    /// </summary>
    [Fact]
    public async Task ADM_07_45_tai_khoan_limit_20_ra_20_20_5_keyset_on_dinh()
    {
        var prefix = NewPrefix("adm07");
        var cs = factory.ConnectionString;
        var baseTime = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var expected = new List<(Guid Id, DateTimeOffset CreatedAt)>();
        for (var i = 0; i < 45; i++)
        {
            var createdAt = baseTime.AddMinutes(i / 15);
            expected.Add((await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-{i:D2}@test.local", createdAt: createdAt), createdAt));
        }
        for (var i = 0; i < 3; i++)
            await IdentitySql.TaoTaiKhoanAsync(cs, $"khac-{prefix}-{i}@test.local", createdAt: baseTime);

        var pages = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var page = await GetOkAsync(ListPath(prefix, 20, cursor));
            pages.Add(page);
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && pages.Count < 5);

        Assert.Equal([20, 20, 5], pages.Select(p => Ids(p).Count));
        Assert.Equal(JsonValueKind.Null, pages[^1].GetProperty("nextCursor").ValueKind);

        // uuid của Postgres so theo byte = so chuỗi "D" theo thứ tự ordinal — viết tay, không đọc lại thứ tự từ DB.
        var expectedOrder = expected
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id.ToString("D"), StringComparer.Ordinal)
            .Select(x => x.Id)
            .ToList();
        Assert.Equal(expectedOrder, pages.SelectMany(Ids));
    }

    /// <summary>
    /// ADM-07b: <c>%</c>, <c>_</c>, <c>\</c> trong <c>q</c> hiểu theo nghĩa đen (<c>LikePattern</c>); <c>q</c> chữ hoa vẫn khớp
    /// (cột <c>citext</c>). Mỗi ký tự đặc biệt có một "bẫy" chỉ khớp khi ký tự đó bị hiểu là đại diện.
    /// </summary>
    [Theory]
    [InlineData("x_", "x_1")]
    [InlineData("y%", "y%1")]
    [InlineData(@"w\", @"w\1")]
    public async Task ADM_07b_ky_tu_dac_biet_theo_nghia_den_khong_phan_biet_hoa_thuong(string suffix, string match)
    {
        var prefix = NewPrefix("adm07b");
        var cs = factory.ConnectionString;
        foreach (var local in new[] { "x_1", "xa1", "y%1", "yz1", @"w\1", "wq1" })
            await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}{local}@test.local");

        Assert.Equal([$"{prefix}{match}@test.local"], Emails(await GetOkAsync(ListPath(prefix + suffix))));
        Assert.Equal([$"{prefix}{match}@test.local"], Emails(await GetOkAsync(ListPath((prefix + suffix).ToUpperInvariant()))));
    }

    /// <summary>
    /// ADM-07c: số câu SQL của một trang KHÔNG phụ thuộc số dòng — trang 20 dòng (có hồ sơ lẫn không) bằng trang 1 dòng. Hydrate
    /// tên hiển thị trong vòng lặp (cạm bẫy 2) làm trang 20 thêm 19 câu.
    /// </summary>
    [Fact]
    public async Task ADM_07c_so_cau_SQL_trang_20_dong_bang_trang_1_dong()
    {
        var prefix = NewPrefix("adm07c");
        var cs = factory.ConnectionString;
        var client = new ModulesTestClient(factory);
        for (var i = 0; i < 21; i++)
        {
            var id = await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-{i:D2}@test.local");
            if (i % 2 == 0)
                await client.PutProfileOkAsync(id, new { displayName = $"Người {i}" });
        }

        using var counter = new SqlCommandCounter(cs);

        // Làm nóng: cache quyền tầng 2, pool, model EF — không thuộc chi phí của trang.
        await GetOkAsync(ListPath(prefix, 1));

        counter.Reset();
        Assert.Single(Ids(await GetOkAsync(ListPath(prefix, 1))));
        var one = counter.Statements.ToList();

        counter.Reset();
        Assert.Equal(20, Ids(await GetOkAsync(ListPath(prefix, 20))).Count);
        var twenty = counter.Statements.ToList();

        Assert.True(one.Count == twenty.Count,
            $"trang 1 dòng: {one.Count} câu, trang 20 dòng: {twenty.Count} câu:\n{string.Join("\n---\n", twenty)}");
        Assert.Equal(2, twenty.Count);   // users ⋈ roles + một lô profiles
    }

    /// <summary>
    /// Hydrate và từng trường của <c>AdminUser</c>: có hồ sơ → <c>displayName</c>, chưa có → <c>null</c>; <c>lockedUntil</c> chỉ khi
    /// còn ở tương lai (mốc đã qua → <c>null</c>); <c>emailVerified</c>; vai trò có cả mã lẫn tên hiển thị.
    /// </summary>
    [Fact]
    public async Task Tung_truong_cua_AdminUser_doc_tu_DB_ten_hydrate_tu_ho_so()
    {
        var prefix = NewPrefix("fields");
        var cs = factory.ConnectionString;
        var future = DateTimeOffset.UtcNow.AddMinutes(10);
        var withProfile = await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-a@test.local", "MODERATOR", lockedUntil: future);
        var noProfile = await IdentitySql.TaoTaiKhoanAsync(
            cs, $"{prefix}-b@test.local", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-10), emailVerified: false);
        await new ModulesTestClient(factory).PutProfileOkAsync(withProfile, new { displayName = "Nguyễn Văn An" });

        var items = (await GetOkAsync(ListPath(prefix))).GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("userId").GetGuid());

        var a = items[withProfile];
        Assert.Equal($"{prefix}-a@test.local", a.GetProperty("email").GetString());
        Assert.Equal("Nguyễn Văn An", a.GetProperty("displayName").GetString());
        Assert.Equal("MODERATOR", a.GetProperty("roleCode").GetString());
        Assert.Equal("Kiểm duyệt viên", a.GetProperty("roleDisplayName").GetString());
        Assert.Equal("active", a.GetProperty("status").GetString());
        Assert.True(a.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(future.ToUnixTimeMilliseconds(), a.GetProperty("lockedUntil").GetDateTimeOffset().ToUnixTimeMilliseconds());

        var b = items[noProfile];
        Assert.Equal(JsonValueKind.Null, b.GetProperty("displayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, b.GetProperty("lockedUntil").ValueKind);   // mốc đã qua = đã tự mở
        Assert.False(b.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("USER", b.GetProperty("roleCode").GetString());

        // Tập trường đúng như AdminUser của hợp đồng — không thêm, không bớt (passwordHash, failedLoginCount…).
        Assert.Equal(
            ["createdAt", "displayName", "email", "emailVerified", "lockedUntil", "roleCode", "roleDisplayName", "status", "userId"],
            a.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>Bộ lọc <c>status</c>, <c>roleCode</c> và kết hợp AND với <c>q</c>. Vai trò đúng dạng mà không tồn tại → trang rỗng, 200.</summary>
    [Fact]
    public async Task Loc_theo_status_va_roleCode_ket_hop_AND()
    {
        var prefix = NewPrefix("filter");
        var cs = factory.ConnectionString;
        var user = await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-u@test.local");
        var disabled = await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-d@test.local", status: "disabled");
        var moderator = await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-m@test.local", "MODERATOR");
        var disabledModerator = await IdentitySql.TaoTaiKhoanAsync(cs, $"{prefix}-dm@test.local", "MODERATOR", "disabled");

        Assert.Equal(new[] { disabled, disabledModerator }.Order(), Ids(await GetOkAsync(ListPath(prefix, extra: "status=disabled"))).Order());
        Assert.Equal(new[] { user, moderator }.Order(), Ids(await GetOkAsync(ListPath(prefix, extra: "status=active"))).Order());
        Assert.Equal(new[] { moderator, disabledModerator }.Order(), Ids(await GetOkAsync(ListPath(prefix, extra: "roleCode=MODERATOR"))).Order());
        Assert.Equal([disabledModerator], Ids(await GetOkAsync(ListPath(prefix, extra: "roleCode=MODERATOR&status=disabled"))));
        Assert.Empty(Ids(await GetOkAsync(ListPath(prefix, extra: "roleCode=KHONG_CO_VAI_TRO"))));
    }

    /// <summary>Chi tiết: 200 cùng hình dạng một dòng của danh sách · id không tồn tại 404 problem+json · id sai dạng 400 <c>errors.userId</c>.</summary>
    [Fact]
    public async Task Chi_tiet_200_khong_ton_tai_404_sai_dang_400()
    {
        var prefix = NewPrefix("detail");
        var id = await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, $"{prefix}@test.local");

        var detail = await GetOkAsync($"/api/v1/admin/users/{id}");
        var fromList = (await GetOkAsync(ListPath(prefix))).GetProperty("items")[0];
        Assert.Equal(fromList.GetRawText(), detail.GetRawText());

        using var missing = await GetAsync($"/api/v1/admin/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(await missing.Content.ReadAsStringAsync()))
            Assert.Equal("Không tìm thấy tài khoản.", problem.RootElement.GetProperty("detail").GetString());

        using var malformed = await GetAsync("/api/v1/admin/users/khong-phai-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var body = JsonDocument.Parse(await malformed.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("userId", out _));
    }

    /// <summary>Sai dạng là 400 theo đúng trường — không sửa âm thầm, không 500.</summary>
    [Theory]
    [InlineData("cursor=khong-phai-cursor", "cursor")]
    [InlineData("cursor=MjAyNnxhYmM", "cursor")]   // base64 hợp lệ, nội dung không phải "thời điểm|uuid"
    [InlineData("limit=0", "limit")]
    [InlineData("limit=51", "limit")]
    [InlineData("status=locked", "status")]
    [InlineData("status=ACTIVE", "status")]
    [InlineData("roleCode=user", "roleCode")]
    [InlineData("roleCode=AB", "roleCode")]
    public async Task Tham_so_sai_dang_400_dung_truong(string query, string field)
    {
        using var response = await GetAsync("/api/v1/admin/users?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal([field], body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Q_qua_254_ky_tu_400()
    {
        using var response = await GetAsync(ListPath(new string('a', 255)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("q", out _));
    }

    /// <summary>
    /// ANY-01 qua endpoint THẬT (bản probe của C4 giữ nguyên): vai trò tự tạo chỉ có MỘT trong ba mã của policy any-of → 200; vai trò
    /// có quyền khác (<c>report.resolve</c>) → 403. Đối chứng hai chiều: policy "đủ cả ba" làm ca đầu đỏ, policy "bất kỳ quyền nào"
    /// làm ca cuối đỏ.
    /// </summary>
    [Theory]
    [InlineData("role.assign", HttpStatusCode.OK)]
    [InlineData("user.unlock", HttpStatusCode.OK)]
    [InlineData("user.lock", HttpStatusCode.OK)]
    [InlineData("report.resolve", HttpStatusCode.Forbidden)]
    public async Task ANY_01_vai_tro_chi_co_mot_ma_cua_policy_xem_duoc_danh_sach(string permission, HttpStatusCode expected)
    {
        var role = IdentitySql.MaVaiTroMoi("ANY");
        await IdentitySql.TaoVaiTroAsync(factory.ConnectionString, role, permission);

        using var list = await GetAsync("/api/v1/admin/users?limit=1", Guid.NewGuid(), role);
        using var detail = await GetAsync($"/api/v1/admin/users/{Guid.NewGuid()}", Guid.NewGuid(), role);

        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, detail.StatusCode);
    }

    /// <summary>MODERATOR (có <c>report.resolve</c>, <c>post.hide</c> nhưng không mã nào của policy) → 403, và để lại dòng audit.</summary>
    [Fact]
    public async Task Moderator_403_va_co_dong_access_denied()
    {
        var moderator = Guid.NewGuid();

        using var response = await GetAsync("/api/v1/admin/users", moderator, "MODERATOR");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select metadata->>'routeTemplate' from moderation.audit_logs where action = 'access.denied' and actor_id = $1", conn);
        cmd.Parameters.AddWithValue(moderator);
        Assert.Equal("api/v1/admin/users", await cmd.ExecuteScalarAsync());
    }
}
