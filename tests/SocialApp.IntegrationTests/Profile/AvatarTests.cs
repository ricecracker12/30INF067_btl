using System.Net;
using System.Net.Http.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Profile;

/// <summary>
/// D3 — <c>PUT</c> + <c>DELETE /users/me/avatar</c>. Ba lớp của Đ-2.8 có một test riêng cho mỗi lớp, vì chúng trả ba mã
/// khác nhau và lẫn lộn thứ tự thì vẫn "có lỗi" mà sai loại lỗi.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AvatarTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Hồ sơ có thật cho <paramref name="actor"/> — mọi nhánh trừ ca Q-D9 đều cần nó.</summary>
    private static Task<ProfileResponse> OnboardAsync(ModulesTestClient client, Guid actor) =>
        client.PutProfileOkAsync(actor, new { displayName = "An Nguyễn" });

    /// <summary>
    /// <c>PROF-04</c> — key dưới tiền tố của NGƯỜI KHÁC → 403 (Đ-2.7). Hai khẳng định quan trọng ngoài mã lỗi:
    /// <c>detail</c> không chứa key (nó là dữ liệu của người khác), và <c>HeadCalls</c> KHÔNG tăng — kiểm tiền tố phải
    /// đứng trước HEAD, nếu không thì mỗi lần kẻ dò gửi một key là một lời gọi R2 có tính tiền.
    /// </summary>
    [Fact]
    public async Task PROF_04_key_cua_nguoi_khac_tra_403_va_khong_ton_mot_luot_HEAD()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        var victimKey = client.PutAvatarObject(Guid.NewGuid());
        var headsBefore = factory.Storage.HeadCalls;

        using var response = await client.SetAvatarAsync(actor, victimKey);
        var (status, title, _) = await ModulesTestClient.ReadProblemAsync(response);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Equal(ProblemTitles.Forbidden, title);
        Assert.DoesNotContain(victimKey, body, StringComparison.Ordinal);
        Assert.Equal(headsBefore, factory.Storage.HeadCalls);
    }

    /// <summary>
    /// Lớp 1 — sai dạng → <b>400</b> với <c>errors.mediaKey</c>, KHÔNG phải 403. <c>posts/{me}/….jpg</c> là ca quan
    /// trọng: nó là key hợp lệ của CHÍNH người gọi, chỉ sai tiền tố loại — nếu regex bị nới thành "bất kỳ key nào của
    /// mình" thì ảnh bài viết gắn được làm avatar.
    /// </summary>
    [Theory]
    [InlineData("avatars/abc.jpg")]
    [InlineData("avatars/khong-phai-guid/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("avatars/{me}/0123456789abcdef0123456789abcdef.gif")]
    [InlineData("posts/{me}/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("")]
    public async Task Key_sai_dang_tra_400_kem_errors_mediaKey(string template)
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        using var response = await client.SetAvatarAsync(actor, template.Replace("{me}", actor.ToString("D"), StringComparison.Ordinal));
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("mediaKey", errors.Keys, StringComparer.Ordinal);
        Assert.Equal(SetAvatarRequestValidator.MediaKeyInvalid, errors["mediaKey"].Single());
    }

    /// <summary>
    /// Lớp 2a — key đúng dạng, đúng của mình, nhưng object CHƯA có trên bucket (client chưa <c>PUT</c> xong) → 400.
    ///
    /// Đây là test integration ĐẦU TIÊN của <c>Error.Validation</c> (Q-D4): lỗi sinh ra sau I/O, trong service, vẫn phải
    /// ra đúng hình dạng 400 của FluentValidation — cùng <c>title</c>, cùng chỗ đặt <c>errors</c>. FE không được thấy hai
    /// hình dạng cho cùng một loại lỗi.
    /// </summary>
    [Fact]
    public async Task Object_chua_ton_tai_tra_400_dung_hinh_dang_cua_Error_Validation()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        // Key đúng dạng nhưng KHÔNG gọi PutObject — chưa có gì trong bucket.
        var key = StorageKeys.ForAvatar(actor, "image/jpeg");

        using var response = await client.SetAvatarAsync(actor, key);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal(
            "Ảnh chưa được tải lên xong. Hãy chờ tải lên hoàn tất rồi thử lại.",
            errors["mediaKey"].Single());
    }

    /// <summary>
    /// Lớp 2b — object CÓ thật nhưng <c>Content-Type</c> ngoài allowlist → 400. Key vẫn đuôi <c>.jpg</c> (đuôi do ta
    /// sinh, không do client chọn), nên lớp 1 cho qua: chỉ HEAD mới nói được sự thật về loại nội dung.
    /// </summary>
    [Fact]
    public async Task Object_sai_loai_tra_400_kem_cau_ve_loai_anh()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        var key = StorageKeys.ForAvatar(actor, "image/jpeg");
        client.PutObject(key, 1024, "text/html");

        using var response = await client.SetAvatarAsync(actor, key);
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("Ảnh đại diện chỉ nhận JPEG, PNG hoặc WebP.", errors["mediaKey"].Single());
    }

    /// <summary>
    /// Nhánh hợp lệ → 200 với <c>avatarUrl</c> là presigned GET của đúng key vừa đặt (Đ-2.9), và <c>GET</c> đọc lại thấy
    /// cùng thứ. Khẳng định URL bắt đầu bằng tiền tố của <see cref="FakeObjectStorage"/> chứ không so nguyên chuỗi: chữ
    /// ký là việc của unit test C2, không phải của test này.
    /// </summary>
    [Fact]
    public async Task Dat_avatar_hop_le_tra_200_voi_avatarUrl_da_ky()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var key = client.PutAvatarObject(actor);

        using var response = await client.SetAvatarAsync(actor, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.Equal($"https://fake.invalid/get/{key}", updated.AvatarUrl);

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{actor:D}/profile");
        get.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var read = await client.Http.SendAsync(get);
        var profile = (await read.Content.ReadFromJsonAsync<ProfileResponse>())!;

        Assert.Equal(updated.AvatarUrl, profile.AvatarUrl);
    }

    /// <summary>
    /// Đ-2.10 — đổi avatar KHÔNG xóa object cũ trong request. <c>Deleted</c> rỗng là cả điểm của quyết định đó: xóa ở
    /// đây thì một lần <c>UPDATE</c> rollback là hồ sơ trỏ vào object đã bốc hơi. Dọn là việc của worker (C4).
    /// </summary>
    [Fact]
    public async Task Doi_avatar_lan_hai_khong_xoa_object_cu()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        var firstKey = client.PutAvatarObject(actor);
        using (var first = await client.SetAvatarAsync(actor, firstKey))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var secondKey = client.PutAvatarObject(actor, "image/png");
        using (var second = await client.SetAvatarAsync(actor, secondKey))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Empty(factory.Storage.Deleted);
        Assert.True(factory.Storage.Exists(firstKey), "object avatar cũ phải còn nguyên trong bucket");
    }

    /// <summary>
    /// <c>DELETE</c> → 204, và <c>GET</c> sau đó thấy <c>avatarUrl: null</c>. Object vẫn còn trên bucket (Đ-2.10) —
    /// "gỡ" ở đây nghĩa là gỡ liên kết, không phải xóa ảnh.
    /// </summary>
    [Fact]
    public async Task Go_avatar_tra_204_va_GET_thay_avatarUrl_null()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);
        var key = client.PutAvatarObject(actor);

        using (var set = await client.SetAvatarAsync(actor, key))
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using (var removed = await client.RemoveAvatarAsync(actor))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{actor:D}/profile");
        get.Headers.Authorization = ModulesTestClient.Bearer(actor);
        using var read = await client.Http.SendAsync(get);
        var profile = (await read.Content.ReadFromJsonAsync<ProfileResponse>())!;

        Assert.Null(profile.AvatarUrl);
        Assert.True(factory.Storage.Exists(key), "gỡ liên kết KHÔNG được xóa object (Đ-2.10)");
        Assert.Empty(factory.Storage.Deleted);
    }

    /// <summary>
    /// Idempotent theo hợp đồng: <c>DELETE</c> lần hai vẫn 204, và <c>DELETE</c> khi CHƯA CÓ hồ sơ cũng 204 — khác
    /// <c>PUT</c> (403, Q-D9) vì <c>DELETE</c> không phải trả về hồ sơ nào. Phân biệt hai trường hợp chỉ tổ nói cho
    /// người gọi biết hồ sơ có tồn tại hay không.
    /// </summary>
    [Fact]
    public async Task Go_avatar_lan_hai_va_go_khi_chua_co_ho_so_deu_204()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        await OnboardAsync(client, actor);

        using (var first = await client.RemoveAvatarAsync(actor))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using (var second = await client.RemoveAvatarAsync(actor))
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        using var chuaOnboarding = await client.RemoveAvatarAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NoContent, chuaOnboarding.StatusCode);
    }

    /// <summary>
    /// <b>Q-D9</b> — <c>PUT</c> khi người gọi chưa onboarding → <b>403</b>, cùng <c>Error.Forbidden</c> với ca key của
    /// người khác. Mọi thứ khác đều hợp lệ (key đúng dạng, đúng tiền tố của mình, object có thật, đúng loại) nên test
    /// này cô lập đúng một điều kiện: <c>UPDATE</c> trúng 0 dòng.
    ///
    /// Không thêm mã mới vào hợp đồng, và không phân biệt được với 403 kia từ ngoài — hai câu chữ khác nhau cho cùng một
    /// loại từ chối chính là chỗ rò thông tin.
    /// </summary>
    [Fact]
    public async Task Q_D9_chua_co_ho_so_ma_PUT_avatar_tra_403()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();   // KHÔNG onboarding
        var key = client.PutAvatarObject(actor);

        using var response = await client.SetAvatarAsync(actor, key);
        var (status, title, _) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Equal(ProblemTitles.Forbidden, title);

        var row = await client.QueryRowAsync("SELECT count(*) AS n FROM profile.profiles WHERE user_id = $1", actor);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>Tầng 1 cho cả hai action.</summary>
    [Fact]
    public async Task An_danh_tra_401_o_ca_PUT_lan_DELETE()
    {
        var client = new ModulesTestClient(factory);

        using var put = await client.Http.PutAsJsonAsync(
            "/api/v1/users/me/avatar", new { mediaKey = $"avatars/{Guid.NewGuid():D}/0123456789abcdef0123456789abcdef.jpg" });
        using var delete = await client.Http.DeleteAsync("/api/v1/users/me/avatar");

        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }
}
