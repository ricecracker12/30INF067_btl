using System.Net;
using System.Net.Http.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.Modules.Profile.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Profile;

/// <summary>
/// D2 — <c>PUT /users/me/profile</c> (upsert). Mỗi test dùng một <c>userId</c> riêng: factory sống cả lớp nên database
/// dùng chung, và hồ sơ khóa chính theo user nên hai test cùng id sẽ giẫm lên nhau.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UpsertProfileTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// <c>PROF-01</c> — lần đầu là TẠO, và <c>GET</c> đọc lại đúng thứ vừa ghi. Đọc lại bằng API thật chứ không bằng SQL:
    /// đây là chỗ duy nhất chứng minh hai endpoint của D1 và D2 nói cùng một ngôn ngữ.
    /// </summary>
    [Fact]
    public async Task PROF_01_lan_dau_tao_ho_so_va_GET_doc_lai_dung_du_lieu()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        var created = await client.PutProfileOkAsync(actor, new { displayName = "An Nguyễn", bio = "Thích chụp ảnh phố." });

        Assert.Equal(actor, created.UserId);
        Assert.Equal("An Nguyễn", created.DisplayName);
        Assert.Equal("Thích chụp ảnh phố.", created.Bio);
        Assert.Null(created.AvatarUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{actor:D}/profile");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.Equal(created, read);
    }

    /// <summary>
    /// <c>PROF-02</c> — lần hai là SỬA, không phải tạo thêm. Ba khẳng định, mỗi cái ứng với một cách hỏng của
    /// <c>ON CONFLICT</c>: vẫn đúng một dòng (đếm bằng SQL, API không phân biệt được), <c>updatedAt</c> tiến lên
    /// (thiếu <c>updated_at</c> trong <c>SET</c>), <c>createdAt</c> đứng yên (lỡ đưa <c>created_at</c> vào <c>SET</c>).
    /// </summary>
    [Fact]
    public async Task PROF_02_lan_hai_sua_tai_cho_van_mot_dong_updatedAt_tien_createdAt_dung_yen()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        var first = await client.PutProfileOkAsync(actor, new { displayName = "An", bio = "Ban đầu." });
        var second = await client.PutProfileOkAsync(actor, new { displayName = "An Nguyễn", bio = "Đã sửa." });

        Assert.Equal("An Nguyễn", second.DisplayName);
        Assert.Equal("Đã sửa.", second.Bio);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.True(second.UpdatedAt > first.UpdatedAt, $"updatedAt phải tiến: {first.UpdatedAt:o} → {second.UpdatedAt:o}");

        var row = await client.QueryRowAsync("SELECT count(*) AS n FROM profile.profiles WHERE user_id = $1", actor);
        Assert.Equal(1L, row!["n"]);
    }

    /// <summary>
    /// Đ-2.4 + Đ-2.2: upsert ghi theo <c>sub</c> của token, không theo bất kỳ thứ gì trong body. Người thứ hai gọi cùng
    /// endpoint thì tạo hồ sơ CỦA MÌNH, không đụng hồ sơ người thứ nhất — endpoint <c>me</c> nên không có tầng 3, và
    /// chính vì vậy phải có test chứng minh <c>actorId</c> thật sự lấy từ token.
    /// </summary>
    [Fact]
    public async Task Hai_nguoi_goi_cung_endpoint_ghi_vao_hai_ho_so_khac_nhau()
    {
        var client = new ModulesTestClient(factory);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var a = await client.PutProfileOkAsync(first, new { displayName = "Người thứ nhất" });
        var b = await client.PutProfileOkAsync(second, new { displayName = "Người thứ hai" });

        Assert.Equal(first, a.UserId);
        Assert.Equal(second, b.UserId);

        using var check = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{first:D}/profile");
        check.Headers.Authorization = ModulesTestClient.Bearer(second);
        using var response = await client.Http.SendAsync(check);
        var reread = (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;

        Assert.Equal("Người thứ nhất", reread.DisplayName);
    }

    /// <summary>
    /// Hai tab cùng onboarding. Đọc-rồi-ghi ở store thì cả hai thấy null, cả hai INSERT, và tab sau nhận 500 từ PK
    /// violation — đúng cái <c>ON CONFLICT</c> tồn tại để chặn. Test này là lý do store không được viết theo lối
    /// <c>FindAsync</c> → <c>Add</c>.
    /// </summary>
    [Fact]
    public async Task Hai_PUT_song_song_lan_dau_cua_cung_mot_nguoi_deu_200_va_chi_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        var responses = await Task.WhenAll(
            client.PutProfileAsync(actor, new { displayName = "Tab một" }),
            client.PutProfileAsync(actor, new { displayName = "Tab hai" }));

        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        var row = await client.QueryRowAsync("SELECT count(*) AS n FROM profile.profiles WHERE user_id = $1", actor);
        Assert.Equal(1L, row!["n"]);
    }

    /// <summary>
    /// Đ-2.10 + cạm bẫy của D2: sửa hồ sơ KHÔNG được làm mất avatar. <c>avatar_key</c> nằm ngoài <c>SET</c> của
    /// <c>ON CONFLICT</c>; đưa nó vào là một dòng trông vô hại xóa ảnh người dùng mỗi lần họ đổi tên.
    ///
    /// Viết ở D2 với avatar dựng bằng SQL trực tiếp (lúc đó D3 chưa có endpoint nào đặt được <c>avatar_key</c>); D3 tới
    /// thì đổi sang <c>PUT /users/me/avatar</c> thật, đúng như ghi chú của D2 đã hẹn. Thiếu test này thì đột biến "thêm
    /// <c>avatar_key = EXCLUDED.avatar_key</c>" đi lọt toàn bộ bộ test của D2 — đã thử và nó lọt thật.
    ///
    /// Khẳng định qua <c>avatarUrl</c> của API chứ không qua cột DB: từ D3 trở đi đã có đường đọc hợp lệ, và test đọc
    /// thẳng cột thì vẫn xanh kể cả khi service quên ký URL.
    /// </summary>
    [Fact]
    public async Task Sua_ho_so_khong_lam_mat_avatar_da_dat()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        await client.PutProfileOkAsync(actor, new { displayName = "An", bio = "Ban đầu." });

        var avatarKey = client.PutAvatarObject(actor);
        using (var set = await client.SetAvatarAsync(actor, avatarKey))
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var after = await client.PutProfileOkAsync(actor, new { displayName = "An Nguyễn", bio = "Đã sửa." });

        Assert.Equal($"https://fake.invalid/get/{avatarKey}", after.AvatarUrl);
    }

    /// <summary>
    /// Validator ĐÃ được nối vào đường request (tầng unit lo biên; ở đây chỉ cần chứng minh dây nối tồn tại và key lỗi
    /// là <c>displayName</c> camelCase — FE hiện lỗi dưới đúng trường theo key này, luật frontend Mục 6).
    /// </summary>
    [Theory]
    [InlineData("A")]
    [InlineData("   ")]
    [InlineData("")]
    public async Task DisplayName_sai_do_dai_tra_400_kem_errors_displayName(string displayName)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.PutProfileAsync(Guid.NewGuid(), new { displayName });
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("displayName", errors.Keys, StringComparer.Ordinal);
        Assert.Equal(UpsertProfileRequestValidator.DisplayNameLength, errors["displayName"].Single());
    }

    /// <summary>Quá 50 ký tự sau trim → 400. Tách khỏi Theory trên để không phải nhúng một chuỗi 51 ký tự vào attribute.</summary>
    [Fact]
    public async Task DisplayName_qua_dai_tra_400()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.PutProfileAsync(
            Guid.NewGuid(), new { displayName = new string('a', UserProfile.DisplayNameMaxLength + 1) });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("displayName", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>"  An  "</c> — 2 ký tự sau trim là HỢP LỆ (200, không phải 400), và DB lưu bản ĐÃ trim: hợp đồng đo "sau khi
    /// trim", nên lưu bản thô là <c>GET</c> trả lại khoảng trắng thừa. Ca dễ làm sai nhất của D2.
    /// </summary>
    [Fact]
    public async Task DisplayName_thua_khoang_trang_van_200_va_duoc_luu_da_trim()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        var created = await client.PutProfileOkAsync(actor, new { displayName = "  An  " });

        Assert.Equal("An", created.DisplayName);

        var row = await client.QueryRowAsync("SELECT display_name FROM profile.profiles WHERE user_id = $1", actor);
        Assert.Equal("An", row!["display_name"]);
    }

    /// <summary><c>bio</c> quá 500 ký tự → 400 dưới key <c>bio</c>, không dồn chung vào <c>displayName</c>.</summary>
    [Fact]
    public async Task Bio_qua_dai_tra_400_kem_errors_bio()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.PutProfileAsync(
            Guid.NewGuid(), new { displayName = "An", bio = new string('b', UserProfile.BioMaxLength + 1) });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("bio", errors.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("displayName", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// Q-D3: <c>PUT</c> là thay thế TOÀN PHẦN. Bốn cách nói "không có bio" — vắng mặt, <c>null</c>, <c>""</c>,
    /// <c>"   "</c> — đều phải cho ra <c>bio: null</c>, kể cả khi hồ sơ đang CÓ bio. Ba ca sau là chỗ chuẩn hóa của
    /// service; thiếu nó thì <c>GET</c> trả bốn thứ khác nhau cho cùng một ý.
    /// </summary>
    [Fact]
    public async Task Q_D3_bio_vang_mat_null_rong_hay_toan_khoang_trang_deu_xoa_bio()
    {
        var client = new ModulesTestClient(factory);

        object[] bodies =
        [
            new { displayName = "An" },                        // vắng mặt hẳn
            new { displayName = "An", bio = (string?)null },
            new { displayName = "An", bio = "" },
            new { displayName = "An", bio = "   " },
        ];

        foreach (var body in bodies)
        {
            var actor = Guid.NewGuid();
            var withBio = await client.PutProfileOkAsync(actor, new { displayName = "An", bio = "Có bio từ trước." });
            Assert.Equal("Có bio từ trước.", withBio.Bio);

            var after = await client.PutProfileOkAsync(actor, body);
            Assert.Null(after.Bio);
        }
    }

    /// <summary>
    /// Field lạ → 400 nhờ <c>UnmappedMemberHandling = Disallow</c> ở host (Mục 1.2). Không có gì trong module làm việc
    /// này, nên test phải nằm ở tầng integration: unit test trên validator sẽ xanh dù cấu hình đó bị gỡ.
    /// </summary>
    [Fact]
    public async Task Field_la_trong_body_tra_400()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.PutProfileAsync(Guid.NewGuid(), new { displayName = "An", nickname = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Tầng 1: không token thì không ghi được gì.</summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.Http.PutAsJsonAsync("/api/v1/users/me/profile", new { displayName = "An" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
