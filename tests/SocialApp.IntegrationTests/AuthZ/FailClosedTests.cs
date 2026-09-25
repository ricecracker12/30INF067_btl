using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using Xunit;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// FC-01 (Đ-6.8, GĐ6 C4): Redis không tới được → endpoint <c>[PrivilegedEndpoint]</c> trả 503 Problem Details
/// <c>urn:socialapp:problem:revocation-unavailable</c> (fail-CLOSED), mọi endpoint khác vẫn fail-open như GĐ1 — bảng tin vẫn chạy.
///
/// <see cref="ModulesApiFactory"/> mặc định trỏ Redis vào cổng không ai nghe (<c>ApiFactory.UnreachableRedis</c>) — đúng cảnh cần.
/// Chạy trên probe đặc quyền: lúc C4 chưa có controller admin thật nào (L-C7); bản trên <c>GET /admin/users</c> là của D2.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FailClosedTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseTestServices(s => s.AddControllers().AddApplicationPart(typeof(AuthZApiFactory).Assembly));
        await factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FC_01_Redis_chet_endpoint_dac_quyen_503_endpoint_thuong_van_chay()
    {
        var client = new ModulesTestClient(factory);
        var moderator = Guid.NewGuid();
        await client.PutProfileOkAsync(moderator, new { displayName = "Kiểm duyệt viên" });

        using var http = factory.CreateClient();

        // Đặc quyền + không kiểm được thu hồi → 503, dù MODERATOR CÓ report.resolve (không phải 403 của tầng 2).
        using var privileged = new HttpRequestMessage(HttpMethod.Get, $"/__test/authz/privileged/{Guid.NewGuid()}");
        privileged.Headers.Authorization = ModulesTestClient.Bearer(moderator, "MODERATOR");
        using var denied = await http.SendAsync(privileged);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.Equal("application/problem+json", denied.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.Equal("urn:socialapp:problem:revocation-unavailable", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(503, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("traceId").GetString()));

        // Không đặc quyền → fail-open như GĐ1: probe [Authorize] và bảng tin thật vẫn 200.
        using var plain = new HttpRequestMessage(HttpMethod.Get, "/__test/authz/authenticated");
        plain.Headers.Authorization = ModulesTestClient.Bearer(moderator, "MODERATOR");
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(plain)).StatusCode);

        using var feed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/feed");
        feed.Headers.Authorization = ModulesTestClient.Bearer(moderator, "MODERATOR");
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(feed)).StatusCode);
    }

    /// <summary>Chưa đăng nhập vào endpoint đặc quyền khi Redis chết vẫn là 401 — không có token thì không có gì để kiểm thu hồi.</summary>
    [Fact]
    public async Task Khong_token_van_401_khong_phai_503()
    {
        using var http = factory.CreateClient();
        using var response = await http.GetAsync($"/__test/authz/privileged/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// FC-01 trên endpoint THẬT (D2): <c>GET /admin/users</c> — Admin, người qua mọi mã quyền, vẫn 503 khi Redis chết. Bản probe ở
    /// trên giữ nguyên: nó canh cơ chế, bản này canh rằng controller <c>admin-v1</c> thật sự mang <c>[PrivilegedEndpoint]</c> tới lúc
    /// chạy (reflection của <c>PrivilegedEndpointTests</c> chỉ thấy attribute, không thấy pipeline).
    /// </summary>
    [Fact]
    public async Task FC_01_admin_users_Redis_chet_Admin_cung_503_revocation_unavailable()
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/users");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), "ADMIN");
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:socialapp:problem:revocation-unavailable", problem.RootElement.GetProperty("type").GetString());
    }
}
