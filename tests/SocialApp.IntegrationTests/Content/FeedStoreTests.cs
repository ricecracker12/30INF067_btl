using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// C2 — hai truy vấn của <see cref="IFeedStore"/> trên Postgres thật, và <b>test đối chiếu</b> (Mục 10.4): luật Đ-4.5 có hai
/// bản — <c>WHERE</c> của LATERAL và <see cref="FeedVisibility"/> — nên cùng một bộ dữ liệu đủ tổ hợp phải cho cùng một tập
/// id qua cả hai. Cùng nếp đối chiếu <c>PostVisibility</c> của GĐ2.
///
/// Dựng dữ liệu bằng <see cref="ContentDbContext"/> trực tiếp, không qua API: đây là test của store, và API không tạo được bài
/// <c>hidden</c> (GĐ6 mới có endpoint).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-23T08:00:00Z");

    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Friend = Guid.NewGuid();
    private static readonly Guid Followee = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FeedSources Sources = new(new HashSet<Guid> { Friend }, new HashSet<Guid> { Followee });

    private static async Task<ServiceProvider> MigratedAsync(PostgresFixture postgres)
    {
        var services = new ServiceCollection()
            .AddContentModule(await postgres.CreateDatabaseAsync())
            .AddLogging()
            .BuildServiceProvider();

        await services.MigrateContentModuleAsync();
        return services;
    }

    /// <summary>
    /// 4 tác giả × 3 mức × 3 trạng thái = 36 bài, mỗi bài một mốc thời gian riêng. <c>deleted</c> có mặt để thấy global query
    /// filter vẫn bọc ngoài câu thô.
    /// </summary>
    private static async Task<List<Post>> SeedAllCombinationsAsync(IServiceProvider services)
    {
        var rows = new List<Post>();
        var i = 0;
        foreach (var author in new[] { Me, Friend, Followee, Stranger })
            foreach (var privacy in new[] { PostPrivacy.Public, PostPrivacy.Friends, PostPrivacy.Private })
                foreach (var status in new[] { PostStatus.Published, PostStatus.Hidden, PostStatus.Deleted })
                {
                    var at = T0.AddMinutes(i++);
                    rows.Add(new Post
                    {
                        AuthorId = author,
                        Body = $"{author:N} {privacy} {status}",
                        Privacy = privacy,
                        Status = status,
                        CreatedAt = at,
                        UpdatedAt = at,
                    });
                }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        db.Posts.AddRange(rows);
        await db.SaveChangesAsync();
        return rows;
    }

    private static async Task<IReadOnlyList<Post>> NetworkAsync(
        IServiceProvider services, FeedSources sources, PostCursor? cursor, int take)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFeedStore>()
            .NetworkPageAsync(Me, sources, cursor, take, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<Post>> SuggestedAsync(IServiceProvider services, PostCursor? cursor, int take)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFeedStore>()
            .SuggestedPageAsync(Me, cursor, take, CancellationToken.None);
    }

    /// <summary>
    /// Đối chiếu: tập id SQL trả == tập id <see cref="FeedVisibility.CanSee"/> chọn, và đúng thứ tự mới trước. Kỳ vọng
    /// tuyệt đối (6 bài) viết tay theo bảng Đ-4.5 để một lỗi chung của hai bản không lọt: mình 3, bạn 2, theo dõi 1, lạ 0 —
    /// tất cả <c>published</c>.
    /// </summary>
    [Fact]
    public async Task Mang_luoi_SQL_khop_FeedVisibility_tren_du_to_hop()
    {
        await using var services = await MigratedAsync(postgres);
        var rows = await SeedAllCombinationsAsync(services);

        var page = await NetworkAsync(services, Sources, cursor: null, take: 100);

        var expected = rows
            .Where(p => FeedVisibility.CanSee(p.Privacy, p.Status, p.AuthorId, Me, Sources))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.PostId)
            .ToList();

        Assert.Equal(6, expected.Count);
        Assert.Equal(expected, page.Select(p => p.PostId).ToList());
    }

    /// <summary>Đối chiếu cho feed gợi ý: public + published của người khác, không bài của mình — 3 bài.</summary>
    [Fact]
    public async Task Goi_y_SQL_khop_CanSeeSuggested_tren_du_to_hop()
    {
        await using var services = await MigratedAsync(postgres);
        var rows = await SeedAllCombinationsAsync(services);

        var page = await SuggestedAsync(services, cursor: null, take: 100);

        var expected = rows
            .Where(p => FeedVisibility.CanSeeSuggested(p.Privacy, p.Status, p.AuthorId, Me))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.PostId)
            .ToList();

        Assert.Equal(3, expected.Count);
        Assert.Equal(expected, page.Select(p => p.PostId).ToList());
    }

    /// <summary>
    /// Nguồn rỗng ở truy vấn MẠNG LƯỚI → chỉ bài của chính mình (lvl 3), không trộn gợi ý. Mảng rỗng <c>unnest</c> ra 0 dòng —
    /// truyền null ở đây là lỗi kiểu, không phải 0 dòng.
    /// </summary>
    [Fact]
    public async Task Nguon_rong_chi_con_bai_cua_minh()
    {
        await using var services = await MigratedAsync(postgres);
        await SeedAllCombinationsAsync(services);

        var page = await NetworkAsync(
            services, new FeedSources(new HashSet<Guid>(), new HashSet<Guid>()), cursor: null, take: 100);

        Assert.Equal(3, page.Count);
        Assert.All(page, p => Assert.Equal(Me, p.AuthorId));
    }

    /// <summary>
    /// Keyset qua nhiều nguồn: <c>take</c> cắt đúng, trang sau bắt đầu ngay sau cursor, hai trang không giao nhau và hợp lại
    /// bằng đúng tập kỳ vọng theo thứ tự — lỗi keyset kinh điển (lặp/nhảy cóc ở ranh giới trang) chỉ thấy khi so cả chuỗi.
    /// </summary>
    [Fact]
    public async Task Cursor_cat_trang_qua_nhieu_nguon_khong_lap_khong_sot()
    {
        await using var services = await MigratedAsync(postgres);
        var rows = await SeedAllCombinationsAsync(services);
        var expected = rows
            .Where(p => FeedVisibility.CanSee(p.Privacy, p.Status, p.AuthorId, Me, Sources))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.PostId)
            .ToList();

        var first = await NetworkAsync(services, Sources, cursor: null, take: 4);
        var last = first[^1];
        var second = await NetworkAsync(services, Sources, new PostCursor(last.CreatedAt, last.PostId), take: 4);

        Assert.Equal(4, first.Count);
        Assert.Equal(expected, first.Concat(second).Select(p => p.PostId).ToList());
    }

    /// <summary>Cursor cũng áp cho feed gợi ý: trang sau không chứa bài của trang trước.</summary>
    [Fact]
    public async Task Cursor_cua_feed_goi_y()
    {
        await using var services = await MigratedAsync(postgres);
        var rows = await SeedAllCombinationsAsync(services);
        var expected = rows
            .Where(p => FeedVisibility.CanSeeSuggested(p.Privacy, p.Status, p.AuthorId, Me))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.PostId)
            .ToList();

        var first = await SuggestedAsync(services, cursor: null, take: 2);
        var second = await SuggestedAsync(services, new PostCursor(first[^1].CreatedAt, first[^1].PostId), take: 2);

        Assert.Equal(expected, first.Concat(second).Select(p => p.PostId).ToList());
    }
}
