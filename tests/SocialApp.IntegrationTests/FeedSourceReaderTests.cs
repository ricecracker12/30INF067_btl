using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.Modules.SocialGraph.Infrastructure;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// A5 + L12 (C1) — <see cref="IFeedSourceReader"/> qua DI của <c>AddSocialGraphModule</c>. Redis cổng 1
/// (không tới được) là đường fail-open: test này canh luật nguồn trên DB, không canh cache.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedSourceReaderTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-21T12:00:00Z");

    private static async Task<ServiceProvider> MigratedAsync(PostgresFixture postgres)
    {
        var services = new ServiceCollection()
            .AddSocialGraphModule(await postgres.CreateDatabaseAsync())
            .AddSharedKernelRedis(ApiFactory.UnreachableRedis)
            .AddLogging()
            .BuildServiceProvider();

        await services.MigrateSocialGraphModuleAsync();
        return services;
    }

    private static Friendship Accepted(Guid requester, Guid recipient)
    {
        var row = Friendship.Request(requester, recipient, Now);
        row.Status = FriendshipStatus.Accepted;
        row.AcceptedAt = Now;
        return row;
    }

    [Fact]
    public async Task Ban_be_ca_hai_nua_cap_min_va_max()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        // A nhỏ hơn B → A là min; A lớn hơn C → A là max. Dùng Guid có thứ tự hex tường minh.
        var a = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        var b = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
        var c = Guid.Parse("00000000-0000-0000-0000-000000000009"); // c < a → A là max

        db.Friendships.AddRange(Accepted(a, b), Accepted(a, c));
        await db.SaveChangesAsync();

        var sources = await scope.ServiceProvider.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Equal(2, sources.Friends.Count);
        Assert.Contains(b, sources.Friends);
        Assert.Contains(c, sources.Friends);
        Assert.Empty(sources.FollowingOnly);
    }

    [Fact]
    public async Task Pending_khong_vao_Friends()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var a = Guid.NewGuid();
        var d = Guid.NewGuid();
        db.Friendships.Add(Friendship.Request(a, d, Now));
        await db.SaveChangesAsync();

        var sources = await scope.ServiceProvider.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.DoesNotContain(d, sources.Friends);
        Assert.True(sources.IsEmpty);
    }

    [Fact]
    public async Task Theo_doi_vao_FollowingOnly()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var a = Guid.NewGuid();
        var e = Guid.NewGuid();
        db.Follows.Add(Follow.Create(a, e, Now));
        await db.SaveChangesAsync();

        var sources = await scope.ServiceProvider.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Empty(sources.Friends);
        Assert.Equal(new HashSet<Guid> { e }, sources.FollowingOnly.ToHashSet());
    }

    [Fact]
    public async Task Ban_va_theo_doi_cung_nguoi_thi_chi_nam_trong_Friends()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        // Id cố định, A là max: bỏ nhánh "tôi là max" thì B rơi sang FollowingOnly — đỏ MỌI lượt, không phải một nửa.
        var a = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
        var b = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        db.Friendships.Add(Accepted(a, b));
        db.Follows.Add(Follow.Create(a, b, Now));
        await db.SaveChangesAsync();

        var sources = await scope.ServiceProvider.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.Contains(b, sources.Friends);
        Assert.DoesNotContain(b, sources.FollowingOnly);
    }

    [Fact]
    public async Task Chieu_theo_doi_nguoc_khong_vao_nguon_cua_A()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();

        var a = Guid.NewGuid();
        var e = Guid.NewGuid();
        db.Follows.Add(Follow.Create(e, a, Now)); // E theo dõi A
        await db.SaveChangesAsync();

        var sources = await scope.ServiceProvider.GetRequiredService<IFeedSourceReader>().GetAsync(a);

        Assert.DoesNotContain(e, sources.FollowingOnly);
        Assert.True(sources.IsEmpty);
    }

    [Fact]
    public async Task Nguoi_moi_thi_IsEmpty()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();

        var sources = await scope.ServiceProvider.GetRequiredService<IFeedSourceReader>()
            .GetAsync(Guid.NewGuid());

        Assert.True(sources.IsEmpty);
    }
}
