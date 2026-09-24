using System.Net;
using System.Text.Json;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using Xunit;

namespace SocialApp.IntegrationTests.Profile;

/// <summary>
/// GĐ6 D12 — <c>GET /search</c> (Đ-6.19, FR-017) trên Postgres thật có <c>unaccent</c>, <c>pg_trgm</c> và index GIN của A4. Hồ sơ dựng qua
/// API thật; tài khoản bị khóa dựng bằng <see cref="IdentitySql"/> (Profile không FK sang Identity — Đ-2.2).
///
/// Cả lớp chung MỘT database: ca nào khẳng định "đúng bằng" thì dùng tên mang token riêng (<see cref="Token"/>) để hồ sơ của ca khác
/// không lọt vào; ca nào dùng tên cố định ("Nguyễn Văn An") thì chỉ khẳng định "có mặt" hoặc thứ tự tương đối.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SearchTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    /// <summary>Sáu chữ cái thường ngẫu nhiên — một "từ" không ai khác có.</summary>
    private static string Token() => new([.. Guid.NewGuid().ToString("N").Where(char.IsLetter).Take(6)]);

    private static async Task<Guid> HoSoAsync(ModulesTestClient client, string ten, Guid? id = null)
    {
        var userId = id ?? Guid.NewGuid();
        await client.PutProfileOkAsync(userId, new { displayName = ten });
        return userId;
    }

    private static async Task<HttpResponseMessage> TimAsync(ModulesTestClient client, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/search" + query);
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        return await client.Http.SendAsync(request);
    }

    private static async Task<List<(Guid Id, string Ten, string? Anh)>> KetQuaAsync(ModulesTestClient client, string q, int limit = 20)
    {
        using var response = await TimAsync(client, $"?q={Uri.EscapeDataString(q)}&limit={limit}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return [.. document.RootElement.GetProperty("items").EnumerateArray().Select(i => (
            i.GetProperty("userId").GetGuid(),
            i.GetProperty("displayName").GetString()!,
            i.GetProperty("avatarUrl").GetString()))];
    }

    // ---------------------------------------------------------------- Mục 10.1

    /// <summary>
    /// <c>SRCH-01..03</c>: "nguyen" ra "Nguyễn Văn An"; "van" cũng ra (tiền tố của từ THỨ HAI); "duc" ra cả "Đức Phạm" lẫn "Lê Đức Anh" ("đ"
    /// qua <c>unaccent</c>). Không phân biệt hoa thường. Đối chứng: "guyen" KHÔNG ra — khớp tiền tố của từ, không phải chuỗi con.
    /// </summary>
    [Fact]
    public async Task SRCH_01_03_khong_dau_tien_to_tung_tu()
    {
        var client = new ModulesTestClient(factory);
        var an = await HoSoAsync(client, "Nguyễn Văn An");
        var ducPham = await HoSoAsync(client, "Đức Phạm");
        var leDucAnh = await HoSoAsync(client, "Lê Đức Anh");

        Assert.Contains(an, (await KetQuaAsync(client, "nguyen")).Select(r => r.Id));
        Assert.Contains(an, (await KetQuaAsync(client, "NGUYEN")).Select(r => r.Id));
        Assert.Contains(an, (await KetQuaAsync(client, "van")).Select(r => r.Id));
        Assert.Contains(an, (await KetQuaAsync(client, "Văn")).Select(r => r.Id));
        var duc = (await KetQuaAsync(client, "duc")).Select(r => r.Id).ToList();
        Assert.Contains(ducPham, duc);
        Assert.Contains(leDucAnh, duc);

        Assert.DoesNotContain(an, (await KetQuaAsync(client, "guyen")).Select(r => r.Id));
    }

    public static TheoryData<string, string> ThamSoSai => new()
    {
        { "?q=a", "q" },
        { "?q=" + new string('x', 51), "q" },
        { "?q=%20%20%20", "q" },
        { "?q=%20a%20", "q" },
        { "", "q" },
        { "?q=an&type=post", "type" },
        { "?q=an&limit=0", "limit" },
        { "?q=an&limit=21", "limit" },
    };

    /// <summary><c>SRCH-04</c>: <c>q</c> sau khi <c>Trim</c> ngắn/dài/rỗng/thiếu → 400 <c>errors.q</c>; <c>type</c> khác <c>user</c>, <c>limit</c> ngoài 1..20.</summary>
    [Theory]
    [MemberData(nameof(ThamSoSai))]
    public async Task SRCH_04_tham_so_sai_400_dung_truong(string query, string field)
    {
        var client = new ModulesTestClient(factory);
        using var response = await TimAsync(client, query);

        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Equal([field], errors.Keys);
    }

    /// <summary>Đối chứng của <c>SRCH-04</c>: đo SAU khi bỏ khoảng trắng — 50 ký tự kèm khoảng trắng hai đầu vẫn 200; <c>type=user</c> 200.</summary>
    [Fact]
    public async Task SRCH_04b_do_sau_khi_trim_va_type_user_hop_le()
    {
        var client = new ModulesTestClient(factory);
        using (var dai = await TimAsync(client, $"?q=%20%20{new string('x', 50)}%20%20"))
            Assert.Equal(HttpStatusCode.OK, dai.StatusCode);
        using (var user = await TimAsync(client, "?q=an&type=user"))
            Assert.Equal(HttpStatusCode.OK, user.StatusCode);
    }

    /// <summary>
    /// <c>SRCH-05</c>: người bị khóa có tên khớp → không có trong kết quả; và trang vẫn ĐỦ <c>limit</c> khi còn ứng viên (lọc sau khi lấy dư,
    /// không cắt <c>limit</c> trước rồi mới lọc).
    /// </summary>
    [Fact]
    public async Task SRCH_05_tai_khoan_bi_khoa_khong_xuat_hien_van_du_limit()
    {
        var client = new ModulesTestClient(factory);
        var t = Token();
        var khoa = await IdentitySql.TaoTaiKhoanAsync(factory.ConnectionString, $"khoa-{t}@test.local", status: "disabled");
        await HoSoAsync(client, $"{t} Anh", khoa);   // "Anh" < "Bình" < "Cường" — người bị khóa đứng đầu thứ tự tên
        var binh = await HoSoAsync(client, $"{t} Bình");
        var cuong = await HoSoAsync(client, $"{t} Cường");

        var ketQua = await KetQuaAsync(client, t, limit: 2);

        Assert.Equal(new[] { binh, cuong }.Order(), ketQua.Select(r => r.Id).Order());
    }

    /// <summary>
    /// <c>SRCH-06</c>: <c>%</c>, <c>_</c> hiểu theo nghĩa đen. "%%" không trả mọi người — chỉ tên bắt đầu (một từ) bằng "%%"; "a_{t}" khớp
    /// "a_{t} Một", KHÔNG khớp "ab{t} Hai" (dưới <c>_</c> ký tự đại diện thì khớp).
    /// </summary>
    [Fact]
    public async Task SRCH_06_ky_tu_dai_dien_hieu_theo_nghia_den()
    {
        var client = new ModulesTestClient(factory);
        var t = Token();
        var phanTram = await HoSoAsync(client, $"%%{t} Phần Trăm");
        var thuong = await HoSoAsync(client, $"Thường {t}");
        var gachDuoi = await HoSoAsync(client, $"a_{t} Một");
        var chuB = await HoSoAsync(client, $"ab{t} Hai");

        var phanTramKq = (await KetQuaAsync(client, "%%")).Select(r => r.Id).ToList();
        Assert.Contains(phanTram, phanTramKq);
        Assert.DoesNotContain(thuong, phanTramKq);
        Assert.All(await KetQuaAsync(client, "%%"), r => Assert.Contains("%%", r.Ten));

        Assert.Equal([gachDuoi], (await KetQuaAsync(client, $"a_{t}")).Select(r => r.Id));
        Assert.Equal([chuB], (await KetQuaAsync(client, $"ab{t}")).Select(r => r.Id));
    }

    /// <summary>
    /// <c>SRCH-07</c> (cạm bẫy 1): <c>EXPLAIN</c> ĐÚNG câu SQL mà endpoint vừa gửi (bắt bằng <see cref="SqlCommandCounter"/>, không chép lại
    /// tay) với <c>SET LOCAL enable_seqscan = off</c> → kế hoạch dùng <c>idx_profiles_display_name_search</c>. Bảng vài chục dòng thì planner
    /// luôn chọn Seq Scan (L-A10), nên ca này chứng minh BIỂU THỨC khớp index; bằng chứng ở quy mô là <c>tests/load/search/explain.sql</c>.
    /// Kèm theo: một lượt tìm là đúng HAI câu SQL (tìm + một lô trạng thái tài khoản).
    /// </summary>
    [Fact]
    public async Task SRCH_07_cau_SQL_cua_endpoint_trung_index_GIN()
    {
        var client = new ModulesTestClient(factory);
        await HoSoAsync(client, "Nguyễn Thị Bích");

        using var counter = new SqlCommandCounter(factory.ConnectionString);
        Assert.NotEmpty(await KetQuaAsync(client, "nguy"));
        var cauTim = Assert.Single(counter.Statements, s => s.Contains("search_norm", StringComparison.Ordinal));
        Assert.Equal(2, counter.Statements.Count);

        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var tat = new NpgsqlCommand("SET LOCAL enable_seqscan = off", conn, tx))
            await tat.ExecuteNonQueryAsync();

        await using var explain = new NpgsqlCommand("EXPLAIN " + cauTim, conn, tx);
        explain.Parameters.Add(new NpgsqlParameter { Value = "nguy", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        explain.Parameters.Add(new NpgsqlParameter { Value = "nguy", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        explain.Parameters.Add(new NpgsqlParameter { Value = 15, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer });
        var keHoach = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                keHoach.Add(reader.GetString(0));

        Assert.Contains(keHoach, l => l.Contains("idx_profiles_display_name_search", StringComparison.Ordinal));
        Assert.DoesNotContain(keHoach, l => l.Contains("Seq Scan", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>SRCH-08</c>: "an" với "An Bình" và "Bảo An" → "An Bình" đứng trước. Rộng hơn: MỌI tên bắt đầu bằng "an" đứng trước mọi tên chỉ có
    /// một từ sau bắt đầu bằng "an" (các ca khác cùng database cũng đóng góp tên — khẳng định thứ tự, không khẳng định tập).
    /// </summary>
    [Fact]
    public async Task SRCH_08_ten_bat_dau_bang_tu_khoa_dung_truoc()
    {
        var client = new ModulesTestClient(factory);
        var baoAn = await HoSoAsync(client, "Bảo An");
        var anBinh = await HoSoAsync(client, "An Bình");

        var ketQua = await KetQuaAsync(client, "an");
        var ids = ketQua.Select(r => r.Id).ToList();

        Assert.True(ids.IndexOf(anBinh) >= 0 && ids.IndexOf(anBinh) < ids.IndexOf(baoAn), string.Join(" · ", ketQua.Select(r => r.Ten)));
        var batDau = ketQua.Select(r => r.Ten.StartsWith("An", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(batDau.OrderByDescending(b => b), batDau);
    }

    // ---------------------------------------------------------------- thêm khi thi công

    /// <summary>Kết quả mang <c>avatarUrl</c> đã ký khi có ảnh, <c>null</c> khi không; không khớp ai → 200 <c>items</c> rỗng (không 404).</summary>
    [Fact]
    public async Task Ket_qua_co_anh_da_ky_khong_khop_thi_rong()
    {
        var client = new ModulesTestClient(factory);
        var t = Token();
        var coAnh = await HoSoAsync(client, $"{t} Có Ảnh");
        using (var avatar = await client.SetAvatarAsync(coAnh, client.PutAvatarObject(coAnh)))
            Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        var khongAnh = await HoSoAsync(client, $"{t} Không Ảnh");

        var ketQua = (await KetQuaAsync(client, t)).ToDictionary(r => r.Id, r => r.Anh);
        Assert.StartsWith("https://", ketQua[coAnh]);
        Assert.Null(ketQua[khongAnh]);

        Assert.Empty(await KetQuaAsync(client, Token() + Token()));
    }
}
