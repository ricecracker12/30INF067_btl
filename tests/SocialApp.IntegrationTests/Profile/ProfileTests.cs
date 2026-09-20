using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Profile;

/// <summary>
/// D1 — <c>GET /users/{userId}/profile</c>. Nhánh 200 vào ở D2 (dựng hồ sơ phải đi qua <c>PUT /users/me/profile</c>, API
/// thật, không phải một câu INSERT trong test — test tự bơm dữ liệu vào DB thì nó đang kiểm store chứ không kiểm endpoint,
/// và sẽ vẫn xanh khi upsert của D2 hỏng).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProfileTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// <c>PROF-03</c> — và là TÍN HIỆU ONBOARDING của FE (Đ-2.4, Mục 7.1). Khẳng định <c>detail</c> KHÔNG chứa
    /// <c>userId</c> dưới cả hai cách viết Guid ("D" có gạch và "N" không gạch): nội suy id vào thông điệp là cách hỏng
    /// dễ xảy ra nhất ở đây ("Người dùng 0192… chưa có hồ sơ"), và nó lọt qua mọi khẳng định chỉ nhìn status code.
    /// </summary>
    [Fact]
    public async Task PROF_03_nguoi_chua_onboarding_tra_404_va_detail_khong_neu_userId()
    {
        var client = new ModulesTestClient(factory);
        var target = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{target:D}/profile");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await client.Http.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.NotFound, status);
        Assert.Equal(ProblemTitles.NotFound, title);
        Assert.Empty(errors);

        using var problem = JsonDocument.Parse(body);
        Assert.Equal("Người dùng này chưa có hồ sơ.", problem.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain($"{target:D}", problem.RootElement.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{target:N}", problem.RootElement.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Người gọi xem hồ sơ CỦA CHÍNH MÌNH khi chưa onboarding cũng phải là 404 — chính là lời gọi FE dùng để quyết định
    /// chuyển sang <c>/onboarding</c>. Trông thừa cạnh test trên nhưng nó chặn một hỏng thật: service "ưu ái" người gọi
    /// (trả 200 với hồ sơ rỗng, hoặc tự tạo hồ sơ) thì FE mất luôn tín hiệu mà không có gì báo.
    /// </summary>
    [Fact]
    public async Task Ho_so_cua_chinh_minh_khi_chua_onboarding_van_404()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{actor:D}/profile");
        request.Headers.Authorization = ModulesTestClient.Bearer(actor);
        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// <c>userId</c> sai dạng → <b>400</b> với <c>errors.userId</c>, KHÔNG phải 404. Đây là lý do route cố ý không mang
    /// ràng buộc <c>{userId:guid}</c> (XML doc của <c>ProfilesController</c>): có ràng buộc thì route không khớp và ra 404,
    /// mà 404 ở endpoint này đã mang nghĩa "chưa onboarding" — FE sẽ đá người dùng sang <c>/onboarding</c> vì một id gõ sai.
    ///
    /// <c>me</c> nằm trong danh sách vì <c>GET /users/me/profile</c> không có trong hợp đồng: nó phải rơi vào
    /// <c>{userId}</c> và ra 400, không được là một endpoint ẩn.
    /// </summary>
    [Theory]
    [InlineData("khong-phai-uuid")]
    [InlineData("me")]
    [InlineData("0192f3c1-8a4e-7c31-9f2a")]
    public async Task UserId_sai_dang_tra_400_kem_errors_userId(string userId)
    {
        var client = new ModulesTestClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{userId}/profile");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await client.Http.SendAsync(request);

        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// D2 — nhánh 200 mà D1 chưa viết được. Hồ sơ CÔNG KHAI trong MVP (Mục 6.1): người đọc là một người khác hẳn, không
    /// phải chủ hồ sơ, và vẫn 200 — không có tầng 3 ở endpoint này.
    ///
    /// <c>avatarUrl</c> phải là <c>null</c> khi chưa đặt avatar (D3 mới đặt được): ký một URL cho <c>avatarKey</c> null
    /// là FE nhận một link hỏng thay vì biết đường hiện ảnh mặc định. <c>bio</c> vắng trong body → <c>null</c>, không
    /// phải chuỗi rỗng (Q-D3).
    /// </summary>
    [Fact]
    public async Task Ho_so_da_onboarding_tra_200_dung_hinh_dang_avatarUrl_null()
    {
        var client = new ModulesTestClient(factory);
        var owner = Guid.NewGuid();
        var created = await client.PutProfileOkAsync(owner, new { displayName = "An Nguyễn" });

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{owner:D}/profile");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.Equal(owner, profile.UserId);
        Assert.Equal("An Nguyễn", profile.DisplayName);
        Assert.Null(profile.Bio);
        Assert.Null(profile.AvatarUrl);
        Assert.Equal(created.CreatedAt, profile.CreatedAt);
    }

    /// <summary>
    /// Tầng 1: hồ sơ công khai TRONG SỐ NGƯỜI ĐÃ ĐĂNG NHẬP (Mục 6.1), không phải công khai với cả Internet. Thay thế
    /// khẳng định 401 của <c>ProfileHarnessTests</c> ở D0 — ở đó nó gắn vào một route chưa tồn tại.
    /// </summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.Http.GetAsync($"/api/v1/users/{Guid.NewGuid():D}/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
