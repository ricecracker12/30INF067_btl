using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Presentation;
using SocialApp.Modules.Profile.Presentation;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Profile;

/// <summary>
/// D0: khẳng định chính cái HARNESS đúng, trước khi có endpoint nào để đổ lỗi. Cùng nếp <c>AuthHarnessTests</c> của GĐ1.
///
/// Test <see cref="Token_USER_qua_tang_1_route_chua_co_404_an_danh_401"/> là test "chưa có controller" — nó ĐỎ khi D2 tới
/// và phải GỠ trong chính commit đó (nếp D1 của GĐ1). Giữ lại sau khi có controller là một test khẳng định điều sai.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProfileHarnessTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Hai khẳng định trong một test vì chúng chỉ có nghĩa cạnh nhau: 404 một mình không phân biệt được "token qua tầng 1,
    /// route chưa có" với "route bị bỏ qua xác thực", còn 401 một mình không nói gì về token.
    ///
    /// 404 ở đây là MỐC TẠM của D0. D2 nối <c>ProfilesController</c> vào thì dòng này chuyển sang 400/200 — và nếu nó VẪN
    /// 404 sau khi controller có thật thì nguyên nhân gần như chắc chắn là thiếu <c>AddApplicationPart</c> trong Program.cs:
    /// hỏng câm duy nhất của D0, không exception, không log, Swagger rỗng.
    /// </summary>
    [Fact]
    public async Task Token_USER_qua_tang_1_route_chua_co_404_an_danh_401()
    {
        var client = new ModulesTestClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/users/me/profile")
        {
            Content = JsonContent.Create(new { displayName = "An", bio = (string?)null }),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());

        using var withToken = await client.Http.SendAsync(request);
        using var anonymous = await client.Http.PutAsJsonAsync("/api/v1/users/me/profile", new { displayName = "An" });

        Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    /// <summary>
    /// Ba chỗ phải khớp tên nhóm (<c>[ApiExplorerSettings]</c> · <c>SwaggerDoc</c> · tên file hợp đồng). D0 mới nối được hai
    /// chỗ sau, và 404 ở đây là cách DUY NHẤT biết mình gõ lệch — Swagger không báo lỗi, nó chỉ thiếu một trang.
    /// <c>paths</c> rỗng là ĐÚNG ở D0: chưa controller nào khai nhóm này.
    /// </summary>
    [Theory]
    [InlineData(ProfileApiGroup.Name)]
    [InlineData(ContentApiGroup.Name)]
    public async Task Trang_swagger_cua_module_ton_tai(string group)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.Http.GetAsync($"/swagger/{group}/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"paths\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// C5 + D0: app thật đang cầm đúng <see cref="FakeObjectStorage"/> của factory, không phải
    /// <c>UnconfiguredObjectStorage</c> mà Program.cs đăng ký khi Development thiếu khóa R2. Sai chỗ này thì mọi test của
    /// D3/D4/D5 đỏ với một exception nói về biến môi trường <c>R2__*</c> — đúng loại triệu chứng chỉ sai hướng.
    /// </summary>
    [Fact]
    public void IObjectStorage_cua_app_la_FakeObjectStorage()
    {
        var storage = factory.Services.GetRequiredService<IObjectStorage>();

        Assert.Same(factory.Storage, storage);
    }

    /// <summary>
    /// <c>PutObject</c> dựng object mà KHÔNG tiêu một lượt HEAD. <c>HeadCalls</c> là thứ D5 dùng để chứng minh "HEAD từng
    /// key, không tin khai báo" (Đ-2.8 lớp 2) — harness tự tăng bộ đếm đó là làm hỏng chính phép đo.
    /// </summary>
    [Fact]
    public void PutObject_khong_lam_tang_HeadCalls()
    {
        var client = new ModulesTestClient(factory);
        var before = factory.Storage.HeadCalls;

        client.PutObject($"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg", 1024, "image/jpeg");

        Assert.Equal(before, factory.Storage.HeadCalls);
    }
}
