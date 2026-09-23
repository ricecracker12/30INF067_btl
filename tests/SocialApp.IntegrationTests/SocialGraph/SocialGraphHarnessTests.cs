using System.Net;
using SocialApp.Modules.SocialGraph.Presentation;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// D0: khẳng định nhóm Swagger đã nối đúng ba chỗ (<c>[ApiExplorerSettings]</c> · <c>SwaggerDoc</c> · tên file hợp đồng)
/// trước khi có controller nào. 404 ở đây là cách DUY NHẤT biết mình gõ lệch — Swagger không báo lỗi, nó chỉ thiếu một
/// trang. <c>paths</c> rỗng là ĐÚNG ở D0.
///
/// Dùng <see cref="ApiFactory"/> (không Postgres): nhóm Swagger ra đời từ <c>apiGroups</c> lúc khởi động, không cần DB.
/// </summary>
public sealed class SocialGraphHarnessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Trang_swagger_cua_module_ton_tai()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync($"/swagger/{SocialGraphApiGroup.Name}/swagger.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"paths\"", body, StringComparison.Ordinal);
    }
}
