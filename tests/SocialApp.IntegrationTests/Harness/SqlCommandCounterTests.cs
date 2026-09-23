using System.Net;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests;

/// <summary>
/// B1 (GĐ4): bộ đếm phải thấy thật sự > 0 lệnh trên một truy vấn biết trước. Luôn ra 0 thì FEED-Q1 "50 = 200" xanh vì
/// 0 = 0 — đúng loại lưới giả mà L4 muốn chặn.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SqlCommandCounterTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dem_ra_lon_hon_0_va_co_cau_content_posts_khi_goi_list_posts()
    {
        var client = new ModulesTestClient(factory);
        var author = Guid.NewGuid();
        await client.PutProfileOkAsync(author, new { displayName = "Tác giả đếm SQL" });
        await client.CreatePostOkAsync(author, new { body = "Một bài để có SELECT.", privacy = "public" });

        using var counter = new SqlCommandCounter(factory.ConnectionString);
        counter.Reset();

        using var response = await client.ListPostsAsync(author, author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(
            counter.Statements.Count > 0,
            "SqlCommandCounter không thấy lệnh nào — tag/nguồn Activity có thể đổi tên. Statements: "
          + string.Join(" | ", counter.Statements));
        Assert.Contains(
            counter.Statements,
            s => s.Contains("content.posts", StringComparison.OrdinalIgnoreCase)
              || s.Contains("\"posts\"", StringComparison.OrdinalIgnoreCase));
    }
}
