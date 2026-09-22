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
/// D3 — <c>POST /friends/requests/{userId}/accept</c>. Test hình dạng: 200 <c>friends</c>, 0 dòng → 403
/// một phản hồi, <c>accepted_at</c> + <c>updated_at</c> cùng câu. Không phải nghiệm thu <c>FRD-05/10</c>
/// của B3 — <c>GET /friends</c> còn là D5.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AcceptFriendRequestTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
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
        client.PutProfileOkAsync(userId, new { displayName = "Người dùng D3" });

    /// <summary>
    /// Nhánh chính. B chấp nhận lời A gửi → 200 <c>friends</c>; dòng <c>accepted</c> với
    /// <c>accepted_at</c> và <c>updated_at</c> khác null; cả hai phía đọc <c>GET /relationships</c>
    /// ra <c>friends</c>.
    /// </summary>
    [Fact]
    public async Task Chap_nhan_tra_200_friends_va_gan_accepted_at()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.AcceptAsync(b, a);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(a, root.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.String, root.GetProperty("friendship").ValueKind);
        Assert.Equal("friends", root.GetProperty("friendship").GetString());
        Assert.False(root.GetProperty("following").GetBoolean());

        var pair = FriendPair.Of(a, b);
        var row = await client.QueryRowAsync(
            """
            select status, accepted_at, updated_at, created_at
            from socialgraph.friendships
            where user_min_id = $1 and user_max_id = $2
            """,
            pair.Min, pair.Max);
        Assert.NotNull(row);
        Assert.Equal("accepted", row["status"]);
        Assert.NotNull(row["accepted_at"]);
        Assert.NotNull(row["updated_at"]);
        Assert.True(ToUtc(row["updated_at"]) >= ToUtc(row["created_at"]));

        using (var fromAResponse = await client.GetRelationshipAsync(a, b))
        {
            Assert.Equal(HttpStatusCode.OK, fromAResponse.StatusCode);
            using var fromA = JsonDocument.Parse(await fromAResponse.Content.ReadAsStringAsync());
            Assert.Equal("friends", fromA.RootElement.GetProperty("friendship").GetString());
        }

        using (var fromBResponse = await client.GetRelationshipAsync(b, a))
        {
            Assert.Equal(HttpStatusCode.OK, fromBResponse.StatusCode);
            using var fromB = JsonDocument.Parse(await fromBResponse.Content.ReadAsStringAsync());
            Assert.Equal("friends", fromB.RootElement.GetProperty("friendship").GetString());
        }
    }

    /// <summary>
    /// Người nhận đã theo dõi từ trước → 200 vẫn <c>friends</c> nhưng <c>following: true</c>.
    /// Độc lập với kết bạn (Đ-4.5).
    /// </summary>
    [Fact]
    public async Task Da_theo_doi_thi_200_friends_kem_following_true()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        var inserted = await client.ExecuteSqlAsync(
            """
            insert into socialgraph.follows (follower_id, followee_id, created_at)
            values ($1, $2, now())
            """,
            b, a);
        Assert.Equal(1, inserted);

        using var response = await client.AcceptAsync(b, a);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("friends", body.RootElement.GetProperty("friendship").GetString());
        Assert.True(body.RootElement.GetProperty("following").GetBoolean());
    }

    /// <summary>
    /// Không có lời mời · tự chấp nhận lời mình gửi · người thứ ba · đã là bạn · chấp nhận lần hai:
    /// cùng 403, cùng câu yaml, không <c>errors</c> nêu lý do. Lời mời pending (nếu có) không đổi.
    /// </summary>
    [Fact]
    public async Task Khong_co_loi_moi_tra_403_khong_noi_ly_do()
    {
        var client = new ModulesTestClient(factory);
        using var response = await client.AcceptAsync(Guid.NewGuid(), Guid.NewGuid());
        await AssertForbiddenWithoutReasonAsync(response);
    }

    [Fact]
    public async Task Tu_chap_nhan_loi_minh_gui_tra_403_va_dong_van_pending()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.AcceptAsync(a, b);
        await AssertForbiddenWithoutReasonAsync(response);
        await AssertStillPendingAsync(client, a, b, requester: a);
    }

    [Fact]
    public async Task Nguoi_thu_ba_tra_403_va_dong_van_pending()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.AcceptAsync(c, a);
        await AssertForbiddenWithoutReasonAsync(response);
        await AssertStillPendingAsync(client, a, b, requester: a);
    }

    [Fact]
    public async Task Da_la_ban_tra_403()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);

        using var response = await client.AcceptAsync(b, a);
        await AssertForbiddenWithoutReasonAsync(response);
    }

    [Fact]
    public async Task Chap_nhan_lan_hai_tra_403()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);

        using var second = await client.AcceptAsync(b, a);
        await AssertForbiddenWithoutReasonAsync(second);

        var pair = FriendPair.Of(a, b);
        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where user_min_id = $1 and user_max_id = $2 and status = 'accepted'",
            pair.Min, pair.Max);
        Assert.Equal(1L, row!["n"]);
    }

    /// <summary>
    /// Chính mình trên route → 400 <c>errors.userId</c>, trước DB. Không phải 403 của 0 dòng,
    /// cũng không 500 vì <c>FriendPair.Of</c> ném.
    /// </summary>
    [Fact]
    public async Task Chinh_minh_tra_400_kem_errors_userId()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.AcceptAsync(actor, actor);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal("Không thể chấp nhận lời mời kết bạn với chính mình.", errors["userId"].Single());
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary><c>userId</c> sai dạng → 400 <c>errors.userId</c>, không 404. Route không <c>:guid</c>.</summary>
    [Theory]
    [InlineData("khong-phai-uuid")]
    [InlineData("0192f3c1-8a4e-7c31-9f2a")]
    public async Task UserId_sai_dang_tra_400_kem_errors_userId(string userId)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.AcceptAsync(Guid.NewGuid(), userId);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>Tầng 2 — vai trò không có <c>friend.respond</c> → 403.</summary>
    [Fact]
    public async Task Vai_tro_thieu_friend_respond_tra_403()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.AcceptAsync(b, a, role: "GUEST");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillPendingAsync(client, a, b, requester: a);
    }

    /// <summary>Tầng 1: chấp nhận chỉ cho người đã đăng nhập.</summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/friends/requests/{Guid.NewGuid():D}/accept");

        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertForbiddenWithoutReasonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(ProblemTitles.Forbidden, root.GetProperty("title").GetString());
        Assert.Equal("Bạn không có quyền thực hiện thao tác này.", root.GetProperty("detail").GetString());
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            Assert.Empty(errors.EnumerateObject());
    }

    /// <summary>
    /// Chấp nhận là thao tác duy nhất của bước 4 làm đổi <c>Friends</c> của nguồn feed (Đ-4.8). 0 dòng (403) để khóa
    /// <c>sg:feed-sources</c> nguyên; chấp nhận thành công xóa khóa của CẢ HAI, và lần đọc kế tiếp thấy bạn mới — không
    /// phải bản cache rỗng còn sống 60s.
    /// </summary>
    [Fact]
    public async Task Chap_nhan_xoa_cache_nguon_ca_hai_phia_0_dong_thi_khong()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);
        await WarmSourcesAsync(a, b);

        using (var self = await client.AcceptAsync(a, b))
            Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.True(await KeyExistsAsync(a));
        Assert.True(await KeyExistsAsync(b));

        using (var accepted = await client.AcceptAsync(b, a))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.False(await KeyExistsAsync(a));
        Assert.False(await KeyExistsAsync(b));

        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IFeedSourceReader>();
        Assert.Contains(b, (await reader.GetAsync(a)).Friends);
        Assert.Contains(a, (await reader.GetAsync(b)).Friends);
    }

    /// <summary>Nạp khóa nguồn của hai người (đang rỗng: lời mời pending không phải bạn). Chép khuôn <c>DeleteFriendshipTests</c>.</summary>
    private async Task WarmSourcesAsync(Guid a, Guid b)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = await scope.ServiceProvider.GetRequiredService<RedisConnection>().GetAsync();
        Assert.True(connection.IsConnected, "Redis của app chưa nối — xóa cache fail-open và ca này xanh vì lý do sai");

        var reader = scope.ServiceProvider.GetRequiredService<IFeedSourceReader>();
        Assert.Empty((await reader.GetAsync(a)).Friends);
        Assert.Empty((await reader.GetAsync(b)).Friends);
        Assert.True(await KeyExistsAsync(a));
        Assert.True(await KeyExistsAsync(b));
    }

    private Task<bool> KeyExistsAsync(Guid userId) =>
        redis.Database.KeyExistsAsync($"sg:feed-sources:{userId:D}");

    private static async Task AssertStillPendingAsync(
        ModulesTestClient client, Guid a, Guid b, Guid requester)
    {
        var pair = FriendPair.Of(a, b);
        var row = await client.QueryRowAsync(
            """
            select requester_id, status, accepted_at
            from socialgraph.friendships
            where user_min_id = $1 and user_max_id = $2
            """,
            pair.Min, pair.Max);
        Assert.NotNull(row);
        Assert.Equal(requester, (Guid)row["requester_id"]!);
        Assert.Equal("pending", row["status"]);
        Assert.Null(row["accepted_at"]);
    }

    private static DateTimeOffset ToUtc(object? value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException($"Không đọc được timestamptz: {value?.GetType().Name ?? "null"}"),
    };
}
