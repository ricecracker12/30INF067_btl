using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.Modules.SocialGraph.Infrastructure;
using SocialApp.SharedKernel.Contracts;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// A5 — <see cref="IFriendshipReader"/> thật qua DI của <c>AddSocialGraphModule</c> trên Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FriendshipReaderTests(PostgresFixture postgres)
{
    private static readonly Guid OppositeA = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid OppositeB = Guid.Parse("01000000-0000-0000-0000-000000000000");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-21T12:00:00Z");

    private static async Task<ServiceProvider> MigratedAsync(PostgresFixture postgres)
    {
        var services = new ServiceCollection()
            .AddSocialGraphModule(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();

        await services.MigrateSocialGraphModuleAsync();
        return services;
    }

    [Fact]
    public async Task Accepted_thi_true_theo_ca_hai_thu_tu_doi_so()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var row = Friendship.Request(a, b, Now);
        row.Status = FriendshipStatus.Accepted;
        row.AcceptedAt = Now;
        db.Friendships.Add(row);
        await db.SaveChangesAsync();

        var reader = scope.ServiceProvider.GetRequiredService<IFriendshipReader>();
        Assert.True(await reader.AreFriendsAsync(a, b));
        Assert.True(await reader.AreFriendsAsync(b, a));
    }

    [Fact]
    public async Task Pending_thi_false_theo_ca_hai_thu_tu()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        db.Friendships.Add(Friendship.Request(a, b, Now));
        await db.SaveChangesAsync();

        var reader = scope.ServiceProvider.GetRequiredService<IFriendshipReader>();
        Assert.False(await reader.AreFriendsAsync(a, b));
        Assert.False(await reader.AreFriendsAsync(b, a));
    }

    [Fact]
    public async Task Khong_co_dong_thi_false()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IFriendshipReader>();
        Assert.False(await reader.AreFriendsAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Cung_mot_id_thi_false_khong_nem()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();

        var me = Guid.NewGuid();
        var reader = scope.ServiceProvider.GetRequiredService<IFriendshipReader>();
        Assert.False(await reader.AreFriendsAsync(me, me));
    }

    [Fact]
    public async Task Cap_doi_nghich_accepted_thi_true()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var row = Friendship.Request(OppositeA, OppositeB, Now);
        row.Status = FriendshipStatus.Accepted;
        row.AcceptedAt = Now;
        db.Friendships.Add(row);
        await db.SaveChangesAsync();

        var reader = scope.ServiceProvider.GetRequiredService<IFriendshipReader>();
        Assert.True(await reader.AreFriendsAsync(OppositeA, OppositeB));
        Assert.True(await reader.AreFriendsAsync(OppositeB, OppositeA));
    }
}
