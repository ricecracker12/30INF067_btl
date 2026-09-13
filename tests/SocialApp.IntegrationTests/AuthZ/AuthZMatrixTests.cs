using System.Net;
using System.Net.Http.Headers;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// KHUNG của AuthZ matrix — chạy từng dòng của <see cref="AuthZMatrix.Cases"/> trên app thật + Postgres thật
/// đã migrate + seed (Đ3: không stub nguồn quyền). Không ai sửa file này để thêm dòng.
/// </summary>
[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class AuthZMatrixTests(PostgresFixture postgres, AuthZApiFactory factory)
    : IClassFixture<AuthZApiFactory>, IAsyncLifetime
{
    private string _db = null!;

    public async Task InitializeAsync()
    {
        // Matrix chỉ ĐỌC dữ liệu nền nên dùng chung một database đã seed cho mọi dòng.
        _db = await postgres.SeededIdentityDatabaseAsync("authz");
        factory.UseDatabase(_db);   // phải trước CreateClient đầu tiên — host dựng lúc đó
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(AuthZMatrix.Ids), MemberType = typeof(AuthZMatrix))]
    public async Task Ma_tran_phan_quyen(string id)
    {
        var c = AuthZMatrix.Cases.Single(x => x.Id == id);
        var client = factory.CreateClient();
        var path = c.ArrangePath is null ? c.Path : await c.ArrangePath(new AuthZArrange(client, _db));

        using var request = new HttpRequestMessage(c.Method, path);
        if (TestJwt.ForCaller(c.Caller) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        // Message tự viết thay cho Assert.Equal: fail thì log nói thẳng mã nào, tình huống gì.
        Assert.True(response.StatusCode == c.Expected,
            $"{c.Id} — {c.Scenario}: mong đợi {(int)c.Expected}, nhận {(int)response.StatusCode}.");

        // Lỗi 401/403 cũng phải là RFC 7807 (AGENTS.md Mục 9) — không phải body rỗng.
        if (c.Expected is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Ma_tran_khong_rong_va_ma_khong_trung()
    {
        Assert.NotEmpty(AuthZMatrix.Cases);
        var duplicated = AuthZMatrix.Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicated);
    }
}
