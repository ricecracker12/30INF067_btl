using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// C4 — thứ tự bảy bước của <see cref="FeedService"/> (Mục 7.2) bằng fake trong bộ nhớ: khi nào thử cache, khi nào ghi, dấu
/// nguồn lệch thì trượt, <c>nextCursor</c> từ danh sách GỐC, kiểm lại BR-02 cả khi trúng lẫn trượt, timeout → 503. Hành vi
/// qua HTTP trên Redis/Postgres thật là việc của <c>FEED-*</c> (B4); ở đây canh logic rẽ nhánh cho rẻ.
/// </summary>
public sealed class FeedServiceTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Friend = Guid.NewGuid();
    private static readonly Guid Followee = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-23T10:00:00Z");

    private static readonly FeedSources Network = new(new HashSet<Guid> { Friend }, new HashSet<Guid> { Followee });
    private static readonly FeedSources Empty = new(new HashSet<Guid>(), new HashSet<Guid>());

    private readonly FakeSources _sources = new();
    private readonly FakeStore _store = new();
    private readonly FakeCache _cache = new();
    private readonly FakePosts _posts = new();

    private FeedService Service()
    {
        var mapper = new PostResponseMapper(new SigningOnlyStorage(), NullLogger<PostResponseMapper>.Instance);
        var hydrator = new PostHydrator(_posts, new EveryoneDirectory(), mapper);
        return new FeedService(_sources, _store, _cache, _posts, hydrator, NullLogger<FeedService>.Instance);
    }

    /// <summary>Bài của <paramref name="author"/>, <paramref name="minutesAgo"/> phút trước mốc — số càng nhỏ càng mới.</summary>
    private static Post PostBy(Guid author, int minutesAgo, PostPrivacy privacy = PostPrivacy.Public) => new()
    {
        AuthorId = author,
        Body = $"Bài {minutesAgo}",
        Privacy = privacy,
        CreatedAt = T0.AddMinutes(-minutesAgo),
        UpdatedAt = T0.AddMinutes(-minutesAgo),
    };

    private static List<Post> NewestFirst(int count, Guid author) =>
        [.. Enumerable.Range(0, count).Select(i => PostBy(author, i))];

    [Fact]
    public async Task Truot_cache_trang_dau_doc_DB_ghi_bon_truong_next_tu_dong_thu_limit()
    {
        _sources.Value = Network;
        var rows = NewestFirst(21, Friend);   // limit + 1
        _store.Rows = rows;

        var result = await Service().GetAsync(Me, null, FeedService.DefaultLimit, CancellationToken.None);

        var page = result.Value!;
        Assert.Equal(FeedMode.Network, page.Mode);
        Assert.Equal(20, page.Items.Count);
        Assert.Equal(new PostCursor(rows[19].CreatedAt, rows[19].PostId).Encode(), page.NextCursor);
        Assert.Equal(21, _store.LastTake);

        var written = Assert.Single(_cache.Sets);
        Assert.Equal(rows.Take(20).Select(p => p.PostId), written.Ids);
        Assert.Equal(FeedMode.Network, written.Mode);
        Assert.Equal(page.NextCursor, written.Next);
        Assert.Equal(FeedFingerprint.Of(Network), written.Fingerprint);
    }

    [Fact]
    public async Task Trung_cache_dau_khop_khong_goi_truy_van_feed_nap_theo_PK_giu_thu_tu_cache()
    {
        _sources.Value = Network;
        var rows = NewestFirst(3, Friend);
        _posts.Stored.AddRange(rows);
        _cache.Stored = new CachedFeedPage(
            [rows[0].PostId, rows[1].PostId, rows[2].PostId], FeedMode.Network, "cursor-cu", FeedFingerprint.Of(Network));

        var page = (await Service().GetAsync(Me, null, FeedService.DefaultLimit, CancellationToken.None)).Value!;

        Assert.Equal(0, _store.Calls);
        Assert.Equal(1, _posts.FindManyCalls);
        Assert.Equal(rows.Select(p => p.PostId), page.Items.Select(i => i.PostId));   // FakePosts trả ngược — service sắp lại
        Assert.Equal("cursor-cu", page.NextCursor);
        Assert.Empty(_cache.Sets);
    }

    /// <summary>Q-C1: nguồn đã đổi từ lúc ghi cache (vừa kết bạn) → coi như trượt, đọc lại và ghi đè.</summary>
    [Fact]
    public async Task Dau_nguon_lech_thi_coi_nhu_truot()
    {
        _sources.Value = Network;
        _store.Rows = NewestFirst(2, Friend);
        _cache.Stored = new CachedFeedPage([], FeedMode.Suggested, null, FeedFingerprint.Of(Empty));

        var page = (await Service().GetAsync(Me, null, FeedService.DefaultLimit, CancellationToken.None)).Value!;

        Assert.Equal(1, _store.Calls);
        Assert.Equal(FeedMode.Network, page.Mode);
        Assert.Equal(2, page.Items.Count);
        Assert.Single(_cache.Sets);
    }

    [Theory]
    [InlineData(true, FeedService.DefaultLimit)]   // có cursor
    [InlineData(false, 10)]                         // limit khác mặc định
    public async Task Chi_trang_dau_voi_limit_mac_dinh_moi_dung_cache(bool withCursor, int limit)
    {
        _sources.Value = Network;
        _store.Rows = NewestFirst(3, Friend);
        var cursor = withCursor ? new PostCursor(T0.AddDays(1), Guid.NewGuid()).Encode() : null;

        await Service().GetAsync(Me, cursor, limit, CancellationToken.None);

        Assert.Equal(0, _cache.Gets);
        Assert.Empty(_cache.Sets);
        Assert.Equal(1, _store.Calls);
    }

    /// <summary>Đ-4.6: nguồn rỗng → truy vấn gợi ý, <c>mode = suggested</c>.</summary>
    [Fact]
    public async Task Nguon_rong_thi_feed_goi_y()
    {
        _sources.Value = Empty;
        var stranger = Guid.NewGuid();
        _store.Rows = NewestFirst(2, stranger);

        var page = (await Service().GetAsync(Me, null, FeedService.DefaultLimit, CancellationToken.None)).Value!;

        Assert.Equal(FeedMode.Suggested, page.Mode);
        Assert.Equal(1, _store.SuggestedCalls);
        Assert.Equal(2, page.Items.Count);
    }

    /// <summary>
    /// Đ-4.9, FEED-09b ở tầng unit: trúng cache, nhưng bài của người CHỈ theo dõi vừa đổi sang <c>friends</c> — bước kiểm lại
    /// loại nó; <c>next</c> vẫn là cursor đã cache (trang ngắn hơn limit là hợp lệ).
    /// </summary>
    [Fact]
    public async Task Trung_cache_van_kiem_lai_BR02_bang_nguon_hien_tai()
    {
        _sources.Value = Network;
        var stillPublic = PostBy(Followee, 1);
        var nowFriendsOnly = PostBy(Followee, 2, PostPrivacy.Friends);
        _posts.Stored.AddRange([stillPublic, nowFriendsOnly]);
        _cache.Stored = new CachedFeedPage(
            [stillPublic.PostId, nowFriendsOnly.PostId], FeedMode.Network, "cursor-cu", FeedFingerprint.Of(Network));

        var page = (await Service().GetAsync(Me, null, FeedService.DefaultLimit, CancellationToken.None)).Value!;

        Assert.Equal([stillPublic.PostId], page.Items.Select(i => i.PostId));
        Assert.Equal("cursor-cu", page.NextCursor);
    }

    /// <summary>
    /// Kiểm lại chạy cả khi TRƯỢT, và <c>next</c> tính từ danh sách GỐC chứ không từ danh sách đã lọc — neo vào danh sách
    /// đã lọc thì trang sau lặp lại đúng bài bị loại (cạm bẫy 1 của C4).
    /// </summary>
    [Fact]
    public async Task Truot_cache_kiem_lai_va_next_tu_danh_sach_goc()
    {
        _sources.Value = Network;
        var rows = NewestFirst(3, Friend);
        rows[1] = PostBy(Friend, 1, PostPrivacy.Private);   // store "lỡ" trả bài private của bạn — bài cuối của trang limit 2
        _store.Rows = rows;

        var page = (await Service().GetAsync(Me, null, 2, CancellationToken.None)).Value!;

        Assert.Equal([rows[0].PostId], page.Items.Select(i => i.PostId));
        Assert.Equal(new PostCursor(rows[1].CreatedAt, rows[1].PostId).Encode(), page.NextCursor);
    }

    /// <summary>Đ-4.10: truy vấn feed quá hạn → 503, không ghi cache, không ném.</summary>
    [Fact]
    public async Task Timeout_thanh_503_khong_ghi_cache()
    {
        _sources.Value = Network;
        _store.Throw = new FeedQueryTimeoutException(new TimeoutException());

        var result = await Service().GetAsync(Me, null, FeedService.DefaultLimit, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(503, result.Error!.Value.Status);
        Assert.Equal(ContentErrors.FeedOverloadedType, result.Error!.Value.Type);
        Assert.Empty(_cache.Sets);
    }

    private sealed class FakeSources : IFeedSourceReader
    {
        public FeedSources Value { get; set; } = Empty;

        public Task<FeedSources> GetAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Value);
    }

    private sealed class FakeStore : IFeedStore
    {
        public List<Post> Rows { get; set; } = [];
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public int SuggestedCalls { get; private set; }
        public int LastTake { get; private set; }

        public Task<IReadOnlyList<Post>> NetworkPageAsync(
            Guid me, FeedSources sources, PostCursor? cursor, int take, CancellationToken ct) => Page(take);

        public Task<IReadOnlyList<Post>> SuggestedPageAsync(Guid me, PostCursor? cursor, int take, CancellationToken ct)
        {
            SuggestedCalls++;
            return Page(take);
        }

        private Task<IReadOnlyList<Post>> Page(int take)
        {
            Calls++;
            LastTake = take;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult<IReadOnlyList<Post>>([.. Rows.Take(take)]);
        }
    }

    private sealed class FakeCache : IFeedPageCache
    {
        public CachedFeedPage? Stored { get; set; }
        public int Gets { get; private set; }
        public List<CachedFeedPage> Sets { get; } = [];

        public Task<CachedFeedPage?> GetAsync(Guid userId, CancellationToken ct)
        {
            Gets++;
            return Task.FromResult(Stored);
        }

        public Task SetAsync(Guid userId, CachedFeedPage page, CancellationToken ct)
        {
            Sets.Add(page);
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(Guid authorId, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Chỉ hai hàm feed chạm tới; còn lại ném để lộ ngay nếu service gọi thứ không nên gọi.</summary>
    private sealed class FakePosts : IPostStore
    {
        public List<Post> Stored { get; } = [];
        public int FindManyCalls { get; private set; }

        /// <summary>Trả NGƯỢC thứ tự — hợp đồng nói "không theo thứ tự nào", service phải tự sắp lại.</summary>
        public Task<IReadOnlyList<Post>> FindManyPublishedAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct)
        {
            FindManyCalls++;
            return Task.FromResult<IReadOnlyList<Post>>(
                [.. Stored.Where(p => postIds.Contains(p.PostId)).Reverse()]);
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachment>>> MediaOfAsync(
            IReadOnlyCollection<Guid> postIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachment>>>(
                new Dictionary<Guid, IReadOnlyList<MediaAttachment>>());

        public Task<bool> AddWithMediaAsync(Post post, IReadOnlyList<MediaAttachment> attachments, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Post?> FindAsync(Guid postId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<Post>> ListByAuthorAsync(
            Guid authorId, Guid actorId, bool areFriends, PostCursor? cursor, int take, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Post?> FindForUpdateAsync(Guid postId, CancellationToken ct) => throw new NotSupportedException();

        public Task SaveAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class EveryoneDirectory : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserCard>>(
                userIds.ToDictionary(id => id, id => new UserCard(id, "Người dùng", null)));
    }

    private sealed class SigningOnlyStorage : IObjectStorage
    {
        public string CreatePresignedGet(string key) => $"https://signed.invalid/{key}";

        public string CreatePresignedPut(string key, string contentType, long contentLength) =>
            throw new NotSupportedException();

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(
            string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
