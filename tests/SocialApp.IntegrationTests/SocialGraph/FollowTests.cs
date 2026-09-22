using System.Net;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.SocialGraph;

/// <summary>
/// B3 — nghiệm thu FR-012 qua HTTP trên Postgres thật (<c>FOL-01..04</c>, Mục 10.1).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FollowTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task OnboardAsync(ModulesTestClient client, Guid userId) =>
        client.PutProfileOkAsync(userId, new { displayName = "Người dùng B3" });

    /// <summary>Theo dõi → 204; <c>GET /relationships</c> ra <c>following: true</c>.</summary>
    [Fact]
    public async Task FOL_01_theo_doi_204_va_relationship_following_true()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);

        using var response = await client.FollowAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var relationship = await client.GetRelationshipOkAsync(a, b);
        Assert.True(relationship.Following);
    }

    /// <summary>Lần hai vẫn 204, đúng một dòng.</summary>
    [Fact]
    public async Task FOL_02_theo_doi_lan_hai_204_dung_mot_dong()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);

        await client.FollowOkAsync(a, b);

        using var second = await client.FollowAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(1L, await FollowCountAsync(client, a, b));
    }

    /// <summary>Tự theo dõi → 400, không 500.</summary>
    [Fact]
    public async Task FOL_03_tu_theo_doi_tra_400_khong_500()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();

        using var response = await client.FollowAsync(a, a);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var row = await client.QueryRowAsync(
            "select count(*) as n from socialgraph.follows where follower_id = $1", a);
        Assert.Equal(0L, row!["n"]);
    }

    /// <summary>Bỏ theo dõi → 204; bỏ lần hai vẫn 204.</summary>
    [Fact]
    public async Task FOL_04_bo_theo_doi_204_va_lan_hai_van_204()
    {
        var client = new ModulesTestClient(factory);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await OnboardAsync(client, b);
        await client.FollowOkAsync(a, b);

        using var first = await client.UnfollowAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(0L, await FollowCountAsync(client, a, b));

        using var second = await client.UnfollowAsync(a, b);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    private static async Task<long> FollowCountAsync(ModulesTestClient client, Guid followerId, Guid followeeId)
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
}
