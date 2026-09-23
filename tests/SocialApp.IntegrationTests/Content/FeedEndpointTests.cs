using System.Net;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D7 — hình dạng của <c>GET /feed</c> trên dây: <c>mode</c> là chuỗi chữ thường, <c>nextCursor</c> là <c>null</c> chứ không
/// phải <c>""</c>, <c>limit</c> ngoài <c>1..50</c> → 400 <c>errors.limit</c>. Ma trận quyền, cache, 503 là bộ nghiệm thu
/// <c>FEED-*</c> (B4) — không lặp ở đây.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedEndpointTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// JSON thô, không qua DTO: DTO đọc bằng converter CamelCase của test thì app ghi <c>"Suggested"</c> test vẫn xanh
    /// (bài học Q-D2 của GĐ2). Người mới không kết nối, không ai có bài → gợi ý rỗng.
    /// </summary>
    [Fact]
    public async Task Mode_la_chuoi_thuong_nextCursor_null_items_mang()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.GetFeedAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("suggested", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("items").ValueKind);
    }

    /// <summary>Có một người theo dõi (chưa đăng gì) → <c>network</c> trên dây.</summary>
    [Fact]
    public async Task Co_mot_ket_noi_thi_mode_network()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await client.PutProfileOkAsync(b, new { displayName = "Người được theo dõi" });
        await client.FollowOkAsync(a, b);

        using var response = await client.GetFeedAsync(a);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("network", json.RootElement.GetProperty("mode").GetString());
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=51")]
    public async Task Limit_ngoai_1_50_tra_400_errors_limit(string query)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.GetFeedAsync(Guid.NewGuid(), query);
        var problem = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal(400, problem.Status);
        Assert.Equal(["Số bài mỗi trang phải từ 1 đến 50."], problem.Errors["limit"]);
    }

    [Theory]
    [InlineData("?limit=1")]
    [InlineData("?limit=50")]
    public async Task Bien_cua_limit_van_200(string query)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.GetFeedAsync(Guid.NewGuid(), query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
