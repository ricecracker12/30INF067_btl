using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.SocialGraph.Application;
using SocialApp.Modules.SocialGraph.Application.Relationships;
using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;
using Xunit;

namespace SocialApp.UnitTests.SocialGraph;

/// <summary>
/// Đ-4.15: xóa cache nguồn chạy SAU <c>COMMIT</c> và không được bỏ dở vì client ngắt kết nối. Store giả coi như câu SQL đã
/// COMMIT rồi token của request mới bị hủy; cache giả làm đúng như <c>FeedSourceCache</c> thật — ném khi token đã hủy.
/// Truyền token của request xuống <c>InvalidateAsync</c> là sáu ca này đỏ.
/// </summary>
public sealed class RelationshipServicePostCommitTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    public static TheoryData<string> WriteOperations => new()
    {
        "send", "accept", "decline", "unfriend", "follow", "unfollow",
    };

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task Client_ngat_sau_COMMIT_van_xoa_cache_nguon(string operation)
    {
        var cache = new RecordingFeedSourceCache();
        var service = new RelationshipService(
            new CommittedStore(),
            new EveryoneExists(),
            cache,
            new SocialGraphEvents(NullLogger<SocialGraphEvents>.Instance),
            TimeProvider.System,
            new NoStorage());

        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        Task committed = operation switch
        {
            "send" => service.SendRequestAsync(Actor, new CreateFriendRequest { UserId = Other }, aborted.Token),
            "accept" => service.AcceptRequestAsync(Actor, Other, aborted.Token),
            "decline" => service.DeclineOrCancelAsync(Actor, Other, aborted.Token),
            "unfriend" => service.UnfriendAsync(Actor, Other, aborted.Token),
            "follow" => service.FollowAsync(Actor, Other, aborted.Token),
            "unfollow" => service.UnfollowAsync(Actor, Other, aborted.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        await committed;

        var call = Assert.Single(cache.Calls);
        Assert.Equal(Actor, call.UserId);
    }

    private sealed class RecordingFeedSourceCache : IFeedSourceCache
    {
        public List<(Guid UserId, Guid? OtherUserId)> Calls { get; } = [];

        public Task InvalidateAsync(Guid userId, Guid? otherUserId = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add((userId, otherUserId));
            return Task.CompletedTask;
        }
    }

    /// <summary>Mọi câu ghi "đã COMMIT" (đổi đúng một dòng); đọc không có gì. Bỏ qua token như câu SQL đã xong.</summary>
    private sealed class CommittedStore : IRelationshipStore
    {
        public Task<Friendship?> FindFriendshipAsync(FriendPair pair, CancellationToken ct) =>
            Task.FromResult<Friendship?>(null);

        public Task<bool> IsFollowingAsync(Guid followerId, Guid followeeId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> AddRequestAsync(Friendship friendship, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> AcceptIncomingAsync(
            FriendPair pair, Guid requesterId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> DeletePendingAsync(FriendPair pair, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> DeleteAcceptedAsync(FriendPair pair, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<FriendListRow>> ListFriendsAsync(
            Guid me, FriendCursor? cursor, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FriendListRow>>([]);

        public Task<IReadOnlyList<FriendListRow>> ListRequestsAsync(
            Guid me, bool incoming, FriendCursor? cursor, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FriendListRow>>([]);

        public Task<bool> AddFollowAsync(Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> DeleteFollowAsync(Guid followerId, Guid followeeId, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class EveryoneExists : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserCard>>(
                userIds.ToDictionary(id => id, id => new UserCard(id, "Người dùng", null)));
    }

    /// <summary>Không thao tác ghi nào ký URL.</summary>
    private sealed class NoStorage : IObjectStorage
    {
        public string CreatePresignedPut(string key, string contentType, long contentLength) =>
            throw new NotSupportedException();

        public string CreatePresignedGet(string key) => throw new NotSupportedException();

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(
            string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
