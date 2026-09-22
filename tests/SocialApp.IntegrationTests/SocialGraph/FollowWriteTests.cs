using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Redis;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// D6 — <c>PUT</c> + <c>DELETE /follows/{userId}</c>. Test hình dạng: 204 idempotent, tự theo dõi 400 trước DB,
/// không hồ sơ 404, chỉ xóa cache của người theo dõi. Không phải nghiệm thu <c>FOL-*</c> của B3.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FollowWriteTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
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
        client.PutProfileOkAsync(userId, new { displayName = "Người dùng D6" });

    /// <summary>
    /// Nhánh chính. 204, đúng một dòng có hướng, <c>GET /relationships</c> ra <c>following: true</c> và
    /// <c>friendship: none</c> — theo dõi không tạo lời mời kết bạn (Đ-4.5).
    /// </summary>
    [Fact]
    public async Task Theo_doi_tra_204_va_dung_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        using var response = await client.FollowAsync(actor, other);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var row = await FollowRowAsync(client, actor, other);
        Assert.NotNull(row);
        Assert.Equal(1L, await FollowCountAsync(client, actor, other));

        var relationship = await client.GetRelationshipOkAsync(actor, other);
        Assert.Equal(FriendshipView.None, relationship.Friendship);
        Assert.True(relationship.Following);
        Assert.Null(await FriendshipRowAsync(client, actor, other));
    }

    /// <summary>
    /// Lần hai vẫn 204, vẫn đúng một dòng. Nguồn là <c>ON CONFLICT DO NOTHING</c>, không phải 409.
    /// Chiều ngược là dòng riêng.
    /// </summary>
    [Fact]
    public async Task Theo_doi_lan_hai_van_204_dung_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, actor);
        await OnboardAsync(client, other);

        await client.FollowOkAsync(actor, other);
        await client.FollowOkAsync(actor, other);
        Assert.Equal(1L, await FollowCountAsync(client, actor, other));

        await client.FollowOkAsync(other, actor);
        Assert.Equal(1L, await FollowCountAsync(client, other, actor));
        Assert.Equal(1L, await FollowCountAsync(client, actor, other));
    }

    /// <summary>
    /// Chính mình → 400 <c>errors.userId</c>, trước DB, không 500. Lọt xuống thì CHECK
    /// <c>ck_follows_not_self</c> thành 500.
    /// </summary>
    [Fact]
    public async Task Tu_theo_doi_tra_400_khong_dong_khong_500()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.FollowAsync(actor, actor);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal("Không thể theo dõi chính mình.", errors["userId"].Single());
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.follows where follower_id = $1 or followee_id = $1",
            actor);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>Người được theo dõi chưa onboarding (Đ-2.4) → 404, cùng câu yaml. Không dòng nào.</summary>
    [Fact]
    public async Task Khong_ho_so_tra_404()
    {
        var client = new ModulesTestClient(factory);
        var missing = Guid.NewGuid();

        using var response = await client.FollowAsync(Guid.NewGuid(), missing);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProblemTitles.NotFound, document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Không tìm thấy người dùng.", document.RootElement.GetProperty("detail").GetString());

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.follows where followee_id = $1",
            missing);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>Bỏ theo dõi → 204, dòng mất. Lần hai vẫn 204. Chính mình cũng 204 — không có dòng để xóa.</summary>
    [Fact]
    public async Task Bo_theo_doi_tra_204_va_xoa_dong()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);
        await client.FollowOkAsync(actor, other);

        using var response = await client.UnfollowAsync(actor, other);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FollowRowAsync(client, actor, other));

        using var second = await client.UnfollowAsync(actor, other);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        using var self = await client.UnfollowAsync(actor, actor);
        Assert.Equal(HttpStatusCode.NoContent, self.StatusCode);

        var relationship = await client.GetRelationshipOkAsync(actor, other);
        Assert.False(relationship.Following);
    }

    /// <summary>Bỏ theo dõi một người bạn không xóa tình bạn (Đ-4.5).</summary>
    [Fact]
    public async Task Bo_theo_doi_khong_dung_ket_ban()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);
        await client.FollowOkAsync(a, b);

        using var response = await client.UnfollowAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FollowRowAsync(client, a, b));

        var relationship = await client.GetRelationshipOkAsync(a, b);
        Assert.Equal(FriendshipView.Friends, relationship.Friendship);
        Assert.False(relationship.Following);
    }

    /// <summary>
    /// Xóa cache nguồn chỉ của người theo dõi, và chỉ khi có dòng đổi. Người được theo dõi giữ khóa.
    /// Lần <c>PUT</c>/<c>DELETE</c> không đổi dòng thì cả hai khóa còn.
    /// </summary>
    [Fact]
    public async Task Chi_xoa_cache_cua_nguoi_theo_doi()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);
        await WarmSourcesAsync(actor, other);

        await client.FollowOkAsync(actor, other);
        Assert.False(await KeyExistsAsync(actor));
        Assert.True(await KeyExistsAsync(other));

        await WarmSourcesAsync(actor, other);
        await client.FollowOkAsync(actor, other);
        Assert.True(await KeyExistsAsync(actor));
        Assert.True(await KeyExistsAsync(other));

        using (var unfollow = await client.UnfollowAsync(actor, other))
            Assert.Equal(HttpStatusCode.NoContent, unfollow.StatusCode);
        Assert.False(await KeyExistsAsync(actor));
        Assert.True(await KeyExistsAsync(other));

        await WarmSourcesAsync(actor, other);
        using (var again = await client.UnfollowAsync(actor, other))
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.True(await KeyExistsAsync(actor));
        Assert.True(await KeyExistsAsync(other));
    }

    /// <summary>
    /// Tầng 2 — vai trò không có <c>friend.request</c> → 403 khi theo dõi, không dòng.
    /// Bỏ theo dõi là <c>[Authorize]</c> trần (Đ-4.12): cùng người với vai <c>GUEST</c> vẫn 204.
    /// </summary>
    [Fact]
    public async Task Vai_tro_thieu_friend_request_tra_403_khi_theo_doi()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        using var denied = await client.FollowAsync(actor, other, role: "GUEST");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Null(await FollowRowAsync(client, actor, other));

        await client.FollowOkAsync(actor, other);

        using var unfollow = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/follows/{other:D}");
        unfollow.Headers.Authorization = ModulesTestClient.Bearer(actor, "GUEST");
        using var allowed = await client.Http.SendAsync(unfollow);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Null(await FollowRowAsync(client, actor, other));
    }

    [Theory]
    [InlineData("khong-phai-uuid")]
    [InlineData("0192f3c1-8a4e-7c31-9f2a")]
    public async Task UserId_sai_dang_tra_400_ca_hai_endpoint(string userId)
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using (var follow = await client.FollowAsync(actor, userId))
        {
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(follow);
            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Equal(ProblemTitles.BadRequest, title);
            Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
        }

        using (var unfollow = await client.UnfollowAsync(actor, userId))
        {
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(unfollow);
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

        using (var follow = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/follows/{userId:D}"))
        using (var response = await client.Http.SendAsync(follow))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var unfollow = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/follows/{userId:D}"))
        using (var response = await client.Http.SendAsync(unfollow))
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

    private static Task<IReadOnlyDictionary<string, object?>?> FollowRowAsync(
        ModulesTestClient client, Guid followerId, Guid followeeId) =>
        client.QueryRowAsync(
            """
            select follower_id, followee_id
            from socialgraph.follows
            where follower_id = $1 and followee_id = $2
            """,
            followerId, followeeId);

    private static async Task<long> FollowCountAsync(
        ModulesTestClient client, Guid followerId, Guid followeeId)
    {
        var row = await client.QueryRowAsync(
            """
            select count(*) as n
            from socialgraph.follows
            where follower_id = $1 and followee_id = $2
            """,
            followerId, followeeId);
        return (long)row!["n"]!;
    }

    private static Task<IReadOnlyDictionary<string, object?>?> FriendshipRowAsync(
        ModulesTestClient client, Guid a, Guid b)
    {
        var pair = FriendPair.Of(a, b);
        return client.QueryRowAsync(
            """
            select status
            from socialgraph.friendships
            where user_min_id = $1 and user_max_id = $2
            """,
            pair.Min, pair.Max);
    }
}
