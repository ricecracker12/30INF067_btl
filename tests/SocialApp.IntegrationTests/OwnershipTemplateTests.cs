using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SocialApp.IntegrationTests.AuthZ;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests;

/// <summary>
/// C6: khuôn tầng 3 qua đủ ba tầng trên app thật. Bổ sung cho dòng OWN-00 của matrix những khẳng định mà khung
/// matrix không kiểm: 403 có traceId, KHÔNG lộ id đã gửi, chính chủ thì qua, và ADMIN cũng không có lối tắt ở
/// tầng 3 (short-circuit chỉ ở tầng 2 — Mục 3.2). Vai trò viết tay.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OwnershipTemplateTests(PostgresFixture postgres, AuthZApiFactory factory)
    : IClassFixture<AuthZApiFactory>, IAsyncLifetime
{
    // Chỉ ĐỌC dữ liệu nền → dùng chung database đã seed với AuthZ matrix.
    public async Task InitializeAsync() => factory.UseDatabase(await postgres.SeededContentDatabaseAsync("authz"));

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpResponseMessage> GetOwnedAsync(Guid ownerId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/__test/authz/owned/{ownerId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Nguoi_khac_goi_thi_403_problem_json_co_traceId_khong_lo_id()
    {
        var ownerId = Guid.NewGuid();

        using var response = await GetOwnedAsync(ownerId, TestJwt.Create("USER", userId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(403, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(ownerId.ToString(), json.RootElement.GetProperty("detail").GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chinh_chu_goi_thi_200()
    {
        var ownerId = Guid.NewGuid();

        using var response = await GetOwnedAsync(ownerId, TestJwt.Create("USER", userId: ownerId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Admin qua tầng 2 nhờ short-circuit, nhưng tầng 3 KHÔNG có nhánh Admin — tài nguyên của người khác vẫn 403.</summary>
    [Fact]
    public async Task ADMIN_khong_co_loi_tat_o_tang_3()
    {
        using var response = await GetOwnedAsync(Guid.NewGuid(), TestJwt.Create("ADMIN", userId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
