using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Redis;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// D4 — <c>DELETE /friends/requests/{userId}</c> và <c>DELETE /friends/{userId}</c>. Test hình dạng:
/// 204 kể cả 0 dòng; hai endpoint không xóa nhầm trạng thái của nhau; xóa cache chỉ khi có dòng bị xóa.
/// Không phải nghiệm thu <c>FRD-07..09</c> của B3 — <c>GET /friends</c> và hiệu lực lên bài còn là D5 / B3.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeleteFriendshipTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Trước CreateClient: không gọi thì Redis là cổng 1, InvalidateAsync fail-open, ca cache xanh vì lý do sai.
        factory.UseRedis(redis.ConnectionString);
        await factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task OnboardAsync(ModulesTestClient client, Guid userId) =>
        client.PutProfileOkAsync(userId, new { displayName = "Người dùng D4" });

    /// <summary>Người gửi hủy lời của mình → 204, dòng mất. Gửi lại được (cùng đường FRD-07, chưa phải ca B3).</summary>
    [Fact]
    public async Task Nguoi_gui_huy_loi_moi_tra_204_va_xoa_dong()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.DeclineOrCancelAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FriendshipRowAsync(client, a, b));

        var again = await client.SendFriendRequestOkAsync(a, b);
        Assert.Equal(FriendshipView.Outgoing, again.Friendship);
    }

    /// <summary>Người nhận từ chối → cùng endpoint, 204, dòng mất. Chiều nào cũng được.</summary>
    [Fact]
    public async Task Nguoi_nhan_tu_choi_loi_moi_tra_204_va_xoa_dong()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.DeclineOrCancelAsync(b, a);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FriendshipRowAsync(client, a, b));
    }

    /// <summary>Không có lời mời → vẫn 204. Lần hai sau khi đã xóa cũng 204.</summary>
    [Fact]
    public async Task Khong_co_loi_moi_van_tra_204()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using var response = await client.DeclineOrCancelAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);
        await client.DeclineOrCancelOkAsync(a, b);

        using var second = await client.DeclineOrCancelAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Null(await FriendshipRowAsync(client, a, b));
    }

    /// <summary>
    /// Đã là bạn → <c>DELETE /friends/requests</c> vẫn 204 nhưng dòng <c>accepted</c> còn.
    /// Thiếu vế <c>status = pending</c> thì ca này đỏ.
    /// </summary>
    [Fact]
    public async Task Huy_loi_moi_khi_da_la_ban_khong_xoa_quan_he()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);

        using var response = await client.DeclineOrCancelAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var row = await FriendshipRowAsync(client, a, b);
        Assert.NotNull(row);
        Assert.Equal("accepted", row["status"]);
    }

    /// <summary>Hủy kết bạn → 204, dòng mất. Dòng theo dõi riêng không bị xóa theo (Đ-4.5).</summary>
    [Fact]
    public async Task Huy_ket_ban_tra_204_va_xoa_dong_nhung_giu_theo_doi()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);

        var inserted = await client.ExecuteSqlAsync(
            """
            insert into socialgraph.follows (follower_id, followee_id, created_at)
            values ($1, $2, now())
            """,
            a, b);
        Assert.Equal(1, inserted);

        using var response = await client.UnfriendAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FriendshipRowAsync(client, a, b));

        var follow = await client.QueryRowAsync(
            """
            select count(*) as n from socialgraph.follows
            where follower_id = $1 and followee_id = $2
            """,
            a, b);
        Assert.Equal(1L, follow!["n"]);
    }

    /// <summary>
    /// Chỉ có lời mời <c>pending</c> → <c>DELETE /friends</c> vẫn 204 nhưng lời mời còn.
    /// Thiếu vế <c>status = accepted</c> thì ca này đỏ.
    /// </summary>
    [Fact]
    public async Task Huy_ket_ban_khi_chi_co_loi_moi_khong_xoa_loi_moi()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.UnfriendAsync(b, a);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var row = await FriendshipRowAsync(client, a, b);
        Assert.NotNull(row);
        Assert.Equal("pending", row["status"]);
        Assert.Equal(a, (Guid)row["requester_id"]!);
    }

    /// <summary>Không phải bạn → vẫn 204. Lần hai sau khi đã hủy cũng 204.</summary>
    [Fact]
    public async Task Khong_phai_ban_van_tra_204()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using var response = await client.UnfriendAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);
        await client.UnfriendOkAsync(a, b);

        using var second = await client.UnfriendAsync(b, a);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Null(await FriendshipRowAsync(client, a, b));
    }

    /// <summary>
    /// Xóa cache nguồn của cả hai chỉ khi có dòng bị xóa. No-op (sai trạng thái, hoặc không có dòng)
    /// để khóa <c>sg:feed-sources</c> nguyên.
    /// </summary>
    [Fact]
    public async Task Chi_xoa_cache_khi_co_dong_bi_xoa()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);
        await WarmSourcesAsync(a, b);

        using (var unfriend = await client.UnfriendAsync(a, b))
            Assert.Equal(HttpStatusCode.NoContent, unfriend.StatusCode);
        Assert.True(await KeyExistsAsync(a));
        Assert.True(await KeyExistsAsync(b));

        using (var decline = await client.DeclineOrCancelAsync(a, b))
            Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);
        Assert.False(await KeyExistsAsync(a));
        Assert.False(await KeyExistsAsync(b));

        await client.MakeFriendsAsync(a, b);
        await WarmSourcesAsync(a, b);

        using (var decline = await client.DeclineOrCancelAsync(b, a))
            Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);
        Assert.True(await KeyExistsAsync(a));
        Assert.True(await KeyExistsAsync(b));

        using (var unfriend = await client.UnfriendAsync(b, a))
            Assert.Equal(HttpStatusCode.NoContent, unfriend.StatusCode);
        Assert.False(await KeyExistsAsync(a));
        Assert.False(await KeyExistsAsync(b));

        await WarmSourcesAsync(a, b);
        using (var again = await client.UnfriendAsync(a, b))
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.True(await KeyExistsAsync(a));
        Assert.True(await KeyExistsAsync(b));
    }

    [Fact]
    public async Task Chinh_minh_huy_loi_moi_tra_400_kem_errors_userId()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.DeclineOrCancelAsync(actor, actor);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal("Không thể hủy lời mời kết bạn với chính mình.", errors["userId"].Single());
    }

    [Fact]
    public async Task Chinh_minh_huy_ket_ban_tra_400_kem_errors_userId()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.UnfriendAsync(actor, actor);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal("Không thể hủy kết bạn với chính mình.", errors["userId"].Single());
    }

    [Theory]
    [InlineData("khong-phai-uuid")]
    [InlineData("0192f3c1-8a4e-7c31-9f2a")]
    public async Task UserId_sai_dang_tra_400_ca_hai_endpoint(string userId)
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using (var decline = await client.DeclineOrCancelAsync(actor, userId))
        {
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(decline);
            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Equal(ProblemTitles.BadRequest, title);
            Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
        }

        using (var unfriend = await client.UnfriendAsync(actor, userId))
        {
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(unfriend);
            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Equal(ProblemTitles.BadRequest, title);
            Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);
        var userId = Guid.NewGuid();

        using (var decline = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/friends/requests/{userId:D}"))
        using (var response = await client.Http.SendAsync(decline))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var unfriend = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/friends/{userId:D}"))
        using (var response = await client.Http.SendAsync(unfriend))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task WarmSourcesAsync(Guid a, Guid b)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = await scope.ServiceProvider.GetRequiredService<RedisConnection>().GetAsync();
        Assert.True(connection.IsConnected, "Redis của app chưa nối — xóa cache fail-open và ca này xanh vì lý do sai");

        var reader = scope.ServiceProvider.GetRequiredService<IFeedSourceReader>();
        await reader.GetAsync(a);
        await reader.GetAsync(b);
        Assert.True(await KeyExistsAsync(a));
        Assert.True(await KeyExistsAsync(b));
    }

    private Task<bool> KeyExistsAsync(Guid userId) =>
        redis.Database.KeyExistsAsync($"sg:feed-sources:{userId:D}");

    private static Task<IReadOnlyDictionary<string, object?>?> FriendshipRowAsync(
        ModulesTestClient client, Guid a, Guid b)
    {
        var pair = FriendPair.Of(a, b);
        return client.QueryRowAsync(
            """
            select requester_id, status
            from socialgraph.friendships
            where user_min_id = $1 and user_max_id = $2
            """,
            pair.Min, pair.Max);
    }
}
