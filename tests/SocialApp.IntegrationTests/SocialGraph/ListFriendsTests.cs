using System.Net;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Application.Relationships;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// D5 — <c>GET /friends</c> và <c>GET /friends/requests</c>. Test hình dạng: keyset, <c>direction</c> lạ thành
/// 400 <c>errors.direction</c>, thẻ mất hồ sơ vắng mặt mà <c>nextCursor</c> vẫn tính từ dòng gốc.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ListFriendsTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Danh_sach_ban_moi_ket_ban_truoc_va_ky_avatar()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await InsertProfileAsync(client, me, "Tôi");
        await InsertProfileAsync(client, older, "Bạn cũ");
        await InsertProfileAsync(client, newer, "Bạn mới");
        await InsertFriendshipAsync(client, me, older, acceptedAt: T0, requester: me);
        await InsertFriendshipAsync(client, me, newer, acceptedAt: T0.AddHours(2), requester: newer);
        await client.ExecuteSqlAsync(
            "update profile.profiles set avatar_key = $2 where user_id = $1",
            newer, "avatars/newer.png");

        var page = await client.ListFriendsOkAsync(me);

        Assert.Equal(2, page.Items.Count);
        Assert.Null(page.NextCursor);
        Assert.Equal(newer, page.Items[0].User.UserId);
        Assert.Equal("Bạn mới", page.Items[0].User.DisplayName);
        Assert.Equal("https://fake.invalid/get/avatars/newer.png", page.Items[0].User.AvatarUrl);
        Assert.Equal(T0.AddHours(2), page.Items[0].Since);
        Assert.Equal(older, page.Items[1].User.UserId);
        Assert.Null(page.Items[1].User.AvatarUrl);
    }

    [Fact]
    public async Task Loi_moi_pending_khong_lot_vao_danh_sach_ban()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();
        var pending = Guid.NewGuid();
        await InsertProfileAsync(client, me, "Tôi");
        await InsertProfileAsync(client, pending, "Đang chờ");
        await InsertRequestAsync(client, me, pending, createdAt: T0, requester: pending);

        var page = await client.ListFriendsOkAsync(me);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Mac_dinh_20_va_trang_sau_khong_lap()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();
        await InsertProfileAsync(client, me, "Tôi");
        var friends = new Guid[21];
        for (var i = 0; i < friends.Length; i++)
        {
            friends[i] = Guid.NewGuid();
            await InsertProfileAsync(client, friends[i], $"Bạn {i}");
            await InsertFriendshipAsync(client, me, friends[i], acceptedAt: T0.AddMinutes(i), requester: me);
        }

        var page1 = await client.ListFriendsOkAsync(me);
        Assert.Equal(ListFriendsQuery.DefaultLimit, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal(friends[20], page1.Items[0].User.UserId);

        var page2 = await client.ListFriendsOkAsync(me, CursorQuery(page1.NextCursor));
        Assert.Single(page2.Items);
        Assert.Equal(friends[0], page2.Items[0].User.UserId);
        Assert.Null(page2.NextCursor);
        Assert.Empty(page2.Items.Select(c => c.User.UserId).Intersect(page1.Items.Select(c => c.User.UserId)));
    }

    /// <summary>
    /// Dòng thứ <c>limit</c> không có hồ sơ. <c>nextCursor</c> phải neo vào dòng đó, không vào thẻ cuối còn lại —
    /// nếu không, trang sau bỏ sót người đứng sau chỗ lọc.
    /// </summary>
    [Fact]
    public async Task Thieu_ho_so_thi_the_vang_va_nextCursor_van_tu_dong_goc()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();
        var visible = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var laterA = Guid.NewGuid();
        var laterB = Guid.NewGuid();
        await InsertProfileAsync(client, me, "Tôi");
        await InsertProfileAsync(client, visible, "Hiện");
        await InsertProfileAsync(client, laterA, "Sau A");
        await InsertProfileAsync(client, laterB, "Sau B");

        await InsertFriendshipAsync(client, me, laterB, acceptedAt: T0.AddHours(1), requester: me);
        await InsertFriendshipAsync(client, me, laterA, acceptedAt: T0.AddHours(2), requester: me);
        await InsertFriendshipAsync(client, me, missing, acceptedAt: T0.AddHours(3), requester: me);
        await InsertFriendshipAsync(client, me, visible, acceptedAt: T0.AddHours(4), requester: me);

        var page1 = await client.ListFriendsOkAsync(me, "?limit=2");

        Assert.Equal([visible], page1.Items.Select(c => c.User.UserId).ToArray());
        Assert.True(FriendCursor.TryDecode(page1.NextCursor, out var cursor));
        Assert.Equal(missing, cursor.OtherUserId);
        Assert.Equal(T0.AddHours(3), cursor.Since);

        var page2 = await client.ListFriendsOkAsync(me, $"?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        Assert.Equal([laterA, laterB], page2.Items.Select(c => c.User.UserId).ToArray());
        Assert.Null(page2.NextCursor);
    }

    [Fact]
    public async Task Danh_sach_rong_nextCursor_la_null_khong_phai_chuoi_rong()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.ListFriendsAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"nextCursor\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Direction_mac_dinh_incoming_va_outgoing_tach_rieng()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();
        var inbound = Guid.NewGuid();
        var outbound = Guid.NewGuid();
        await InsertProfileAsync(client, me, "Tôi");
        await InsertProfileAsync(client, inbound, "Gửi cho tôi");
        await InsertProfileAsync(client, outbound, "Tôi gửi");
        await InsertRequestAsync(client, me, inbound, createdAt: T0.AddHours(2), requester: inbound);
        await InsertRequestAsync(client, me, outbound, createdAt: T0.AddHours(1), requester: me);
        await InsertFriendshipAsync(client, me, Guid.NewGuid(), acceptedAt: T0.AddHours(3), requester: me);

        var incoming = await client.ListRequestsOkAsync(me);
        Assert.Equal([inbound], incoming.Items.Select(c => c.User.UserId).ToArray());
        Assert.Equal(T0.AddHours(2), incoming.Items[0].Since);

        var outgoing = await client.ListRequestsOkAsync(me, "?direction=outgoing");
        Assert.Equal([outbound], outgoing.Items.Select(c => c.User.UserId).ToArray());
    }

    [Fact]
    public async Task Ban_da_chap_nhan_khong_lot_vao_loi_moi()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();
        var friend = Guid.NewGuid();
        await InsertProfileAsync(client, me, "Tôi");
        await InsertProfileAsync(client, friend, "Bạn");
        await InsertFriendshipAsync(client, me, friend, acceptedAt: T0, requester: friend);

        var page = await client.ListRequestsOkAsync(me);

        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("INCOMING")]
    public async Task Direction_la_tra_400_kem_errors_direction(string direction)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.ListRequestsAsync(Guid.NewGuid(), $"?direction={direction}");
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal(ListFriendRequestsQueryValidator.DirectionInvalid, errors["direction"].Single());
    }

    [Fact]
    public async Task Cursor_rac_tra_400_con_cursor_lech_mui_gio_van_200()
    {
        var client = new ModulesTestClient(factory);
        var me = Guid.NewGuid();

        using (var response = await client.ListFriendsAsync(me, CursorQuery("abc")))
        {
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);
            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Equal(ProblemTitles.BadRequest, title);
            Assert.Equal(ListFriendsQueryValidator.CursorInvalid, errors["cursor"].Single());
        }

        var lech = new FriendCursor(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)), Guid.NewGuid()).Encode();
        using var hopLe = await client.ListFriendsAsync(me, CursorQuery(lech));
        Assert.Equal(HttpStatusCode.OK, hopLe.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("abc")]
    public async Task Limit_ngoai_khoang_tra_400(string limit)
    {
        var client = new ModulesTestClient(factory);

        using var friends = await client.ListFriendsAsync(Guid.NewGuid(), $"?limit={limit}");
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(friends);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("limit", errors.Keys, StringComparer.Ordinal);

        using var requests = await client.ListRequestsAsync(Guid.NewGuid(), $"?limit={limit}");
        var (requestStatus, _, requestErrors) = await ModulesTestClient.ReadProblemAsync(requests);
        Assert.Equal((int)HttpStatusCode.BadRequest, requestStatus);
        Assert.Contains("limit", requestErrors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);

        using (var friends = new HttpRequestMessage(HttpMethod.Get, "/api/v1/friends"))
        using (var response = await client.Http.SendAsync(friends))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var requests = new HttpRequestMessage(HttpMethod.Get, "/api/v1/friends/requests"))
        using (var response = await client.Http.SendAsync(requests))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string CursorQuery(string cursor) => $"?cursor={Uri.EscapeDataString(cursor)}";

    private static Task InsertProfileAsync(ModulesTestClient client, Guid userId, string displayName) =>
        client.ExecuteSqlAsync(
            """
            insert into profile.profiles (user_id, display_name, created_at, updated_at)
            values ($1, $2, now(), now())
            """,
            userId, displayName);

    private static Task InsertFriendshipAsync(
        ModulesTestClient client, Guid a, Guid b, DateTimeOffset acceptedAt, Guid requester)
    {
        var pair = FriendPair.Of(a, b);
        return client.ExecuteSqlAsync(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
            values ($1, $2, $3, 'accepted', $4, $4, $4)
            """,
            pair.Min, pair.Max, requester, acceptedAt);
    }

    private static Task InsertRequestAsync(
        ModulesTestClient client, Guid a, Guid b, DateTimeOffset createdAt, Guid requester)
    {
        var pair = FriendPair.Of(a, b);
        return client.ExecuteSqlAsync(
            """
            insert into socialgraph.friendships
                (user_min_id, user_max_id, requester_id, status, created_at, updated_at)
            values ($1, $2, $3, 'pending', $4, $4)
            """,
            pair.Min, pair.Max, requester, createdAt);
    }
}
