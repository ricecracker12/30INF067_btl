using System.Net;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// B3 — nghiệm thu FR-010/011 và BR-03 qua HTTP trên Postgres thật (<c>FRD-01..10</c>, Mục 10.1).
/// Khẳng định ngoài status code. Không lặp test hình dạng của <c>D1</c>–<c>D5</c> (bốn trạng thái
/// <c>/relationships</c>, phân trang, id sai dạng).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FriendRequestTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task OnboardAsync(ModulesTestClient client, Guid userId) =>
        client.PutProfileOkAsync(userId, new { displayName = "Người dùng B3" });

    /// <summary>US-010 AC-01 nửa đầu: 201 <c>outgoing</c>; B thấy A trong lời mời đến.</summary>
    [Fact]
    public async Task FRD_01_gui_loi_moi_outgoing_va_B_thay_A_trong_incoming()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, a);
        await OnboardAsync(client, b);

        using var response = await client.SendFriendRequestAsync(a, new { userId = b });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("outgoing", body.RootElement.GetProperty("friendship").GetString());

        var incoming = await client.ListRequestsOkAsync(b, "?direction=incoming");
        Assert.Contains(incoming.Items, card => card.User.UserId == a);
    }

    /// <summary>AC-02: gửi lại cùng cặp → 409, vẫn đúng một dòng.</summary>
    [Fact]
    public async Task FRD_02_gui_lai_tra_409_dung_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);

        await client.SendFriendRequestOkAsync(a, b);

        using var second = await client.SendFriendRequestAsync(a, new { userId = b });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1L, await FriendshipCountAsync(client, a, b));
    }

    /// <summary>AC-03: tự gửi → 400 <c>errors.userId</c>, không dòng, không 500.</summary>
    [Fact]
    public async Task FRD_03_tu_gui_tra_400_khong_dong_khong_500()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();

        using var response = await client.SendFriendRequestAsync(a, new { userId = a });
        var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where requester_id = $1", a);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>Id chưa có hồ sơ → 404.</summary>
    [Fact]
    public async Task FRD_04_khong_ho_so_tra_404()
    {
        var client = new ModulesTestClient(factory);
        var missing = Guid.NewGuid();

        using var response = await client.SendFriendRequestAsync(Guid.NewGuid(), new { userId = missing });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where user_min_id = $1 or user_max_id = $1",
            missing);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>AC-01 nửa sau: <c>friends</c>, <c>accepted_at</c> khác null, cả hai thấy nhau trong <c>GET /friends</c>.</summary>
    [Fact]
    public async Task FRD_05_chap_nhan_thanh_ban_va_ca_hai_thay_nhau()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, a);
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var response = await client.AcceptAsync(b, a);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("friends", body.RootElement.GetProperty("friendship").GetString());

        var pair = FriendPair.Of(a, b);
        var row = await client.QueryRowAsync(
            """
            select accepted_at
            from socialgraph.friendships
            where user_min_id = $1 and user_max_id = $2
            """,
            pair.Min, pair.Max);
        Assert.NotNull(row);
        Assert.NotNull(row["accepted_at"]);

        var friendsOfA = await client.ListFriendsOkAsync(a);
        var friendsOfB = await client.ListFriendsOkAsync(b);
        Assert.Contains(friendsOfA.Items, card => card.User.UserId == b);
        Assert.Contains(friendsOfB.Items, card => card.User.UserId == a);
    }

    /// <summary>
    /// Mười cặp mới, mỗi cặp A→B và B→A cùng lúc. Từng cặp: đúng một 201, đúng một 409, đúng một dòng, không 500.
    /// Hai mươi người — rate limit theo user, không gom về một A.
    /// </summary>
    [Fact]
    public async Task FRD_06_muoi_cap_song_song_mot_201_mot_409_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var pairs = Enumerable.Range(0, 10).Select(_ => (A: Guid.NewGuid(), B: Guid.NewGuid())).ToArray();
        foreach (var (a, b) in pairs)
        {
            await OnboardAsync(client, a);
            await OnboardAsync(client, b);
        }

        foreach (var (a, b) in pairs)
        {
            var sent = await Task.WhenAll(
                client.SendFriendRequestAsync(a, new { userId = b }),
                client.SendFriendRequestAsync(b, new { userId = a }));

            try
            {
                var codes = sent.Select(r => (int)r.StatusCode).Order().ToArray();
                Assert.Equal([(int)HttpStatusCode.Created, (int)HttpStatusCode.Conflict], codes);
                Assert.Equal(1L, await FriendshipCountAsync(client, a, b));
            }
            finally
            {
                foreach (var response in sent)
                    response.Dispose();
            }
        }
    }

    /// <summary>B từ chối → 204, dòng mất; A gửi lại → 201.</summary>
    [Fact]
    public async Task FRD_07_B_tu_choi_xoa_dong_roi_A_gui_lai_duoc()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var declined = await client.DeclineOrCancelAsync(b, a);
        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        Assert.Equal(0L, await FriendshipCountAsync(client, a, b));

        using var again = await client.SendFriendRequestAsync(a, new { userId = b });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    /// <summary>A hủy lời mình đã gửi → 204, dòng mất.</summary>
    [Fact]
    public async Task FRD_08_A_huy_loi_moi_xoa_dong()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);

        using var cancelled = await client.DeclineOrCancelAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(0L, await FriendshipCountAsync(client, a, b));
    }

    /// <summary>
    /// Hủy kết bạn → 204. Bài <c>friends</c> của B với A ngay request kế là 404, quan sát qua
    /// <c>GET /posts/{id}</c> — không gọi <c>AreFriendsAsync</c> từ test.
    /// </summary>
    [Fact]
    public async Task FRD_09_huy_ket_ban_roi_bai_friends_thanh_404()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, a);
        await OnboardAsync(client, b);
        await client.MakeFriendsAsync(a, b);

        var post = await client.CreatePostOkAsync(
            b, new { body = "Chỉ bạn bè.", privacy = "friends", mediaKeys = Array.Empty<object>() });

        using (var visible = await client.GetPostAsync(a, post.PostId))
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        using var unfriend = await client.UnfriendAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, unfriend.StatusCode);

        using var hidden = await client.GetPostAsync(a, post.PostId);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    /// <summary>A gửi → A hủy → B chấp nhận → 403. Body không nêu lý do.</summary>
    [Fact]
    public async Task FRD_10_chap_nhan_sau_khi_huy_tra_403_khong_noi_ly_do()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.SendFriendRequestOkAsync(a, b);
        await client.DeclineOrCancelOkAsync(a, b);

        using var response = await client.AcceptAsync(b, a);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(ProblemTitles.Forbidden, root.GetProperty("title").GetString());
        Assert.Equal("Bạn không có quyền thực hiện thao tác này.", root.GetProperty("detail").GetString());
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            Assert.Empty(errors.EnumerateObject());

        Assert.Equal(0L, await FriendshipCountAsync(client, a, b));
    }

    private static async Task<long> FriendshipCountAsync(ModulesTestClient client, Guid a, Guid b)
    {
        var pair = FriendPair.Of(a, b);
        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.friendships where user_min_id = $1 and user_max_id = $2",
            pair.Min, pair.Max);
        return (long)row!["n"]!;
    }
}
