using System.Net;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// D1 — <c>GET /relationships/{userId}</c>. Test hình dạng của endpoint: bốn giá trị <c>friendship</c> × hai giá trị
/// <c>following</c> dựng bằng SQL (endpoint ghi chưa có), chính mình → 400, người không tồn tại → 200
/// <c>none</c>/<c>false</c>. Không phải nghiệm thu BR-03 — đó là <c>FRD-*</c> của B3.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RelationshipTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Mười hai tổ hợp là thừa: bốn trạng thái bạn bè độc lập với hai trạng thái theo dõi (Đ-4.5). Đọc JSON thô
    /// chứ không qua DTO — <c>JsonStringEnumConverter</c> mặc định vẫn nhận số, nên deserialize enum không bắt được
    /// cạm bẫy D0 ("<c>FriendshipView</c> ra JSON dạng số").
    /// </summary>
    [Theory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("outgoing", false)]
    [InlineData("outgoing", true)]
    [InlineData("incoming", false)]
    [InlineData("incoming", true)]
    [InlineData("friends", false)]
    [InlineData("friends", true)]
    public async Task Bon_trang_thai_friendship_nhan_following(string friendship, bool following)
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(client, actor, other, friendship, following);

        using var response = await client.GetRelationshipAsync(actor, other);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(other, root.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.String, root.GetProperty("friendship").ValueKind);
        Assert.Equal(friendship, root.GetProperty("friendship").GetString());
        Assert.Equal(following, root.GetProperty("following").GetBoolean());
    }

    /// <summary>
    /// Hỏi quan hệ với chính mình → 400 <c>errors.userId</c>, câu chép D0. Không có quan hệ nào với chính mình để hỏi
    /// (hợp đồng); <c>FriendPair.Of</c> cũng ném nếu luồng này lọt xuống store.
    /// </summary>
    [Fact]
    public async Task Chinh_minh_tra_400_kem_errors_userId()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.GetRelationshipAsync(actor, actor);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Equal("Không thể xem quan hệ với chính mình.", errors["userId"].Single());
    }

    /// <summary>
    /// Người không tồn tại (Guid mới, không hồ sơ, không dòng quan hệ) → 200 <c>none</c>/<c>false</c>, không 404.
    /// Endpoint đọc quan hệ không phải chỗ dò ai có tài khoản (Mục 8.1).
    /// </summary>
    [Fact]
    public async Task Nguoi_khong_ton_tai_tra_200_none_false()
    {
        var client = new ModulesTestClient(factory);
        var missing = Guid.NewGuid();

        using var response = await client.GetRelationshipAsync(Guid.NewGuid(), missing);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(missing, root.GetProperty("userId").GetGuid());
        Assert.Equal("none", root.GetProperty("friendship").GetString());
        Assert.False(root.GetProperty("following").GetBoolean());
    }

    /// <summary>
    /// <c>userId</c> sai dạng → 400 <c>errors.userId</c>, không 404. Route cố ý không ràng buộc <c>:guid</c>
    /// (XML doc của <c>RelationshipsController</c>).
    /// </summary>
    [Theory]
    [InlineData("khong-phai-uuid")]
    [InlineData("0192f3c1-8a4e-7c31-9f2a")]
    public async Task UserId_sai_dang_tra_400_kem_errors_userId(string userId)
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.GetRelationshipAsync(Guid.NewGuid(), userId);
        var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemTitles.BadRequest, title);
        Assert.Contains("userId", errors.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// Tầng 1: quan hệ chỉ cho người đã đăng nhập. Thay thế khẳng định 401 của harness D0 — ở đó nó gắn vào một
    /// route chưa tồn tại.
    /// </summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.Http.GetAsync($"/api/v1/relationships/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Dựng một cặp quan hệ bằng SQL. Dùng <see cref="FriendPair.Of"/> — tự <c>CompareTo</c> tại chỗ là mở lại
    /// GUID-01. <c>incoming</c> = người kia là requester; <c>outgoing</c>/<c>friends</c> = actor là requester.
    /// </summary>
    private static async Task SeedAsync(
        ModulesTestClient client, Guid actor, Guid other, string friendship, bool following)
    {
        if (friendship is not "none")
        {
            var pair = FriendPair.Of(actor, other);
            var requester = friendship == "incoming" ? other : actor;
            var inserted = friendship == "friends"
                ? await client.ExecuteSqlAsync(
                    """
                    insert into socialgraph.friendships
                        (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
                    values ($1, $2, $3, 'accepted', now(), now(), now())
                    """,
                    pair.Min, pair.Max, requester)
                : await client.ExecuteSqlAsync(
                    """
                    insert into socialgraph.friendships
                        (user_min_id, user_max_id, requester_id, status, created_at, updated_at)
                    values ($1, $2, $3, 'pending', now(), now())
                    """,
                    pair.Min, pair.Max, requester);
            Assert.Equal(1, inserted);
        }

        if (following)
        {
            var inserted = await client.ExecuteSqlAsync(
                """
                insert into socialgraph.follows (follower_id, followee_id, created_at)
                values ($1, $2, now())
                """,
                actor, other);
            Assert.Equal(1, inserted);
        }
    }
}
