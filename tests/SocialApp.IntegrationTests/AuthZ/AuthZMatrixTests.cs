using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        // Matrix chỉ ĐỌC dữ liệu nền nên dùng chung một database đã seed cho mọi dòng. Từ GĐ2 (B1) là database
        // đủ ba module: TC-A03 gọi /api/v1/posts, thiếu bảng content.posts thì dòng đỏ 500 thay vì 403.
        _db = await postgres.SeededContentDatabaseAsync("authz");
        factory.UseDatabase(_db);   // phải trước CreateClient đầu tiên — host dựng lúc đó
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(AuthZMatrix.Ids), MemberType = typeof(AuthZMatrix))]
    public async Task Ma_tran_phan_quyen(string id)
    {
        var c = AuthZMatrix.Cases.Single(x => x.Id == id);
        var client = factory.CreateClient();

        // Q-B2: ký token TRƯỚC khi dựng dữ liệu, rồi truyền chính id đó vào ArrangePath. Thứ tự ngược lại (bản GĐ1) làm
        // hàm dựng dữ liệu không biết người gọi là ai, và TC-A03-media xanh vì lý do sai — xem AuthZArrange.
        var callerUserId = Guid.NewGuid();
        var path = c.ArrangePath is null ? c.Path : await c.ArrangePath(new AuthZArrange(client, _db, callerUserId));

        using var request = new HttpRequestMessage(c.Method, path);
        if (TestJwt.ForCaller(c.Caller, callerUserId) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Endpoint có `requestBody: required` (POST /posts, PATCH /posts/{id}) thì request không body dừng ở model
        // binding với 400 — trước cả tầng 3, tức là dòng không bao giờ chạm tới thứ nó định canh. Xem AuthZCase.Body.
        if (c.Body is not null)
            request.Content = JsonContent.Create(c.Body);

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
