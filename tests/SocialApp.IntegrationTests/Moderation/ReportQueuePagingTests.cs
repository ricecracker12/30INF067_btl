using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// GĐ6 D7b <c>QUE-02</c> — keyset của hàng đợi. Lớp RIÊNG, database riêng (một <see cref="ModulesApiFactory"/> mỗi lớp): "20 / 20 / 5"
/// chỉ khẳng định được khi hàng đợi không có dòng của ca khác (<see cref="ReportQueueTests"/> lọc theo đối tượng của mình, ở đây thì
/// không lọc gì).
///
/// Báo cáo đặt bằng SQL với <c>created_at</c> chọn tay: ba đối tượng một mốc, để khóa phụ <c>target_id</c> của keyset thật sự được
/// dùng — keyset chỉ theo mốc thời gian thì trùng/sót đúng ở những nhóm hòa này. Đối tượng thứ nhất có thêm báo cáo thứ hai (muộn
/// hơn) để khẳng định keyset đi theo <c>min(created_at)</c> của NHÓM, không theo từng báo cáo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportQueuePagingTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task QUE_02_45_doi_tuong_limit_20_ra_20_20_5_khong_trung_khong_sot_dung_thu_tu()
    {
        var client = new ModulesTestClient(factory);
        var goc = new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
        var doiTuong = Enumerable.Range(0, 45).Select(i => (Id: Guid.NewGuid(), At: goc.AddSeconds(i / 3))).ToList();
        foreach (var (id, at) in doiTuong)
            await BaoCaoSqlAsync(client, id, at);
        await BaoCaoSqlAsync(client, doiTuong[0].Id, goc.AddHours(1));   // báo cáo thứ hai, muộn — không đổi vị trí của nhóm

        // Thứ tự Postgres của uuid = thứ tự chuỗi hex, không phải Guid.CompareTo của .NET.
        var mongDoi = doiTuong
            .OrderBy(d => d.At).ThenBy(d => d.Id.ToString("D"), StringComparer.Ordinal)
            .Select(d => d.Id).ToList();

        var trang = new List<List<Guid>>();
        string? cursor = null;
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "/api/v1/reports?limit=20" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
            request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), "MODERATOR");
            using var response = await client.Http.SendAsync(request);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            trang.Add([.. body.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("target").GetProperty("id").GetGuid())]);
            cursor = body.GetProperty("nextCursor").GetString();
        } while (cursor is not null && trang.Count < 5);

        Assert.Equal([20, 20, 5], trang.Select(t => t.Count));
        Assert.Null(cursor);
        Assert.Equal(mongDoi, trang.SelectMany(t => t));
    }

    private static async Task BaoCaoSqlAsync(ModulesTestClient client, Guid target, DateTimeOffset at) =>
        await client.ExecuteSqlAsync(
            "insert into moderation.reports (id, reporter_id, target_type, target_id, reason_code, status, created_at, updated_at) " +
            "values ($1, $2, 'post', $3, 'spam', 'open', $4, $4)",
            Uuid7.New(), Guid.NewGuid(), target, at);
}
