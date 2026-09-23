using System.Net;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// D2 — <c>POST /friends/requests</c>. Test hình dạng của endpoint: 201 <c>outgoing</c>, tự gửi 400 trước
/// DB, không hồ sơ 404, PK <c>23505</c> → 409. Không phải nghiệm thu BR-03 / <c>FRD-*</c> của B3.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SendFriendRequestTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task OnboardAsync(ModulesTestClient client, Guid userId) =>
        client.PutProfileOkAsync(userId, new { displayName = "Người dùng D2" });

    /// <summary>
    /// Nhánh chính. <c>friendship = "outgoing"</c>, đúng một dòng <c>pending</c>, <c>requester</c> là người
    /// gọi; <c>following</c> đọc thật (mặc định chưa theo dõi).
    /// </summary>
    [Fact]
    public async Task Gui_loi_moi_tra_201_outgoing()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        using var response = await client.SendFriendRequestAsync(actor, new { userId = other });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(other, root.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.String, root.GetProperty("friendship").ValueKind);
        Assert.Equal("outgoing", root.GetProperty("friendship").GetString());
        Assert.False(root.GetProperty("following").GetBoolean());

        var pair = FriendPair.Of(actor, other);
        var row = await client.QueryRowAsync(
            """
            select requester_id, status, accepted_at
            from socialgraph.friendships
            where user_min_id = $1 and user_max_id = $2
            """,
            pair.Min, pair.Max);
        Assert.NotNull(row);
        Assert.Equal(actor, (Guid)row["requester_id"]!);
        Assert.Equal("pending", row["status"]);
        Assert.Null(row["accepted_at"]);
    }

    /// <summary>
    /// Người gửi đã theo dõi từ trước → 201 vẫn <c>outgoing</c> nhưng <c>following: true</c>. Độc lập
    /// với kết bạn (Đ-4.5); FE vẽ lại nút từ phản hồi này, không gọi thêm GET.
    /// </summary>
    [Fact]
    public async Task Da_theo_doi_thi_201_outgoing_kem_following_true()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        var inserted = await client.ExecuteSqlAsync(
            """
            insert into socialgraph.follows (follower_id, followee_id, created_at)
            values ($1, $2, now())
            """,
            actor, other);
        Assert.Equal(1, inserted);

        using var response = await client.SendFriendRequestAsync(actor, new { userId = other });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("outgoing", body.RootElement.GetProperty("friendship").GetString());
        Assert.True(body.RootElement.GetProperty("following").GetBoolean());
    }

    /// <summary>
    /// Tự gửi → 400 <c>errors.userId</c>, câu chép yaml. Không dòng nào: kiểm <b>trước</b> DB —
    /// lọt xuống thì <c>FriendPair.Of</c> ném, CHECK <c>ck_friendships_order</c> thành 500.
    /// </summary>
    [Fact]
    public async Task Tu_gui_tra_400_kem_errors_userId_va_khong_dong()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.SendFriendRequestAsync(actor, new { userId = actor });
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal("Không thể gửi lời mời kết bạn cho chính mình.", errors["userId"].Single());

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where requester_id = $1", actor);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>
    /// Người được mời chưa onboarding (Đ-2.4) → 404, cùng câu yaml. Không phải 400 và không dòng nào.
    /// </summary>
    [Fact]
    public async Task Khong_ho_so_tra_404()
    {
        var client = new ModulesTestClient(factory);
        var missing = Guid.NewGuid();

        using var response = await client.SendFriendRequestAsync(Guid.NewGuid(), new { userId = missing });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProblemTitles.NotFound, document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Không tìm thấy người dùng.", document.RootElement.GetProperty("detail").GetString());

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where requester_id = $1 or user_min_id = $1 or user_max_id = $1",
            missing);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>
    /// Cùng cặp lần hai (cùng chiều) → 409, đúng một dòng. Nguồn là PK <c>23505</c>, không phải
    /// SELECT trước INSERT.
    /// </summary>
    [Fact]
    public async Task Gui_lai_cung_cap_tra_409_dung_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        await client.SendFriendRequestOkAsync(actor, other);

        using var second = await client.SendFriendRequestAsync(actor, new { userId = other });
        await AssertConflictAsync(second);

        var pair = FriendPair.Of(actor, other);
        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where user_min_id = $1 and user_max_id = $2",
            pair.Min, pair.Max);
        Assert.Equal(1L, row!["n"]);
    }

    /// <summary>
    /// B đã gửi cho A → A gửi cho B cũng 409 (PK cặp, không phụ thuộc <c>requester_id</c>).
    /// </summary>
    [Fact]
    public async Task Loi_moi_chieu_nguoc_tra_409()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, a);
        await OnboardAsync(client, b);

        await client.SendFriendRequestOkAsync(b, a);

        using var reverse = await client.SendFriendRequestAsync(a, new { userId = b });
        await AssertConflictAsync(reverse);
    }

    /// <summary>Đã là bạn (dòng <c>accepted</c> dựng bằng SQL — accept chưa có) → 409.</summary>
    [Fact]
    public async Task Da_la_ban_tra_409()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        var pair = FriendPair.Of(actor, other);
        var inserted = await client.ExecuteSqlAsync(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
            values ($1, $2, $3, 'accepted', now(), now(), now())
            """,
            pair.Min, pair.Max, actor);
        Assert.Equal(1, inserted);

        using var response = await client.SendFriendRequestAsync(actor, new { userId = other });
        await AssertConflictAsync(response);
    }

    /// <summary><c>userId</c> vắng mặt hoặc rỗng → 400 <c>errors.userId</c>, không 500.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"userId":"00000000-0000-0000-0000-000000000000"}""")]
    public async Task UserId_thieu_hoac_rong_tra_400_kem_errors_userId(string json)
    {
        var client = new ModulesTestClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/friends/requests")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());

        using var response = await client.Http.SendAsync(request);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>Tầng 2 — vai trò không có <c>friend.request</c> → 403.</summary>
    [Fact]
    public async Task Vai_tro_thieu_friend_request_tra_403()
    {
        var client = new ModulesTestClient(factory);
        var other = Guid.NewGuid();
        await OnboardAsync(client, other);

        using var response = await client.SendFriendRequestAsync(
            Guid.NewGuid(), new { userId = other }, role: "GUEST");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Tầng 1: lời mời chỉ cho người đã đăng nhập. Cùng khẳng định với <c>TC-A01-friends</c>.</summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/friends/requests")
        {
            Content = new StringContent(
                $$"""{"userId":"{{Guid.NewGuid():D}}"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProblemTitles.Conflict, document.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "Đã có lời mời hoặc quan hệ bạn bè giữa hai người.",
            document.RootElement.GetProperty("detail").GetString());
    }
}
