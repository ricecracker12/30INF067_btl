using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialApp.IntegrationTests.AuthZ;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Wiring của tầng 1 (C4) — KHÔNG mang trait AuthZ: đây là cấu hình JwtBearer, không phải phân quyền.
///
/// Khóa bẫy "quên MapInboundClaims = false": handler đổi "role" thành URI của ClaimTypes.Role →
/// PermissionHandler (C2) không tìm thấy vai trò và chặn cả Admin. Tên claim "role" viết tay, cố ý không đọc
/// JwtClaims — test dùng chung nguồn với code thì code sai kiểu gì test cũng sai theo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JwtAuthenticationTests(PostgresFixture postgres, AuthZApiFactory factory)
    : IClassFixture<AuthZApiFactory>, IAsyncLifetime
{
    // Chỉ ĐỌC dữ liệu nền → dùng chung database đã seed với AuthZ matrix.
    public async Task InitializeAsync() => factory.UseDatabase(await postgres.SeededContentDatabaseAsync("authz"));

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Principal_giu_ten_claim_ngan()
    {
        var userId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/authz/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("MODERATOR", userId));

        using var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<WhoAmI>();
        Assert.Equal(userId.ToString(), body!.Name);   // Identity.Name == sub (NameClaimType)
        Assert.Equal("MODERATOR", body.Role);          // claim "role" giữ tên ngắn
    }

    private sealed record WhoAmI(string? Name, string? Role);
}
