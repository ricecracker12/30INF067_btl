using Microsoft.Extensions.Logging;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// <c>GET /feed</c> — SEQ-03 (giai-doan-4.md Mục 7.2). Thứ tự các bước là một phần của thiết kế, không phải sở thích:
/// <list type="number">
/// <item>Nguồn ← <see cref="IFeedSourceReader"/> (SocialGraph, cache 60s của chính nó).</item>
/// <item>Nguồn rỗng → <see cref="FeedMode.Suggested"/>, còn lại <see cref="FeedMode.Network"/> — kể cả khi feed rỗng.</item>
/// <item>Trang đầu + <c>limit</c> mặc định → thử <see cref="IFeedPageCache"/>; trúng VÀ dấu nguồn khớp → lấy <c>ids</c>,
/// <c>next</c>.</item>
/// <item>Trượt → <see cref="IFeedStore"/> (timeout 5s → 503); <c>next</c> từ danh sách GỐC; trang đầu thì ghi cache.</item>
/// <item>Bài: trúng cache → nạp theo PK; trượt → dùng luôn các dòng vừa trả (L14 — nạp lại là một câu thừa).</item>
/// <item>Kiểm lại BR-02 bằng nguồn HIỆN TẠI, trong bộ nhớ — KỂ CẢ khi trượt: rẻ, và giữ một đường duy nhất.</item>
/// <item>Hydrate (<see cref="PostHydrator"/>) — mọi lần, kể cả khi trúng: URL ký mới, <c>canEdit</c> theo người xem.</item>
/// </list>
/// </summary>
public sealed class FeedService(
    IFeedSourceReader sourceReader,
    IFeedStore store,
    IFeedPageCache pageCache,
    IPostStore posts,
    PostHydrator hydrator,
    ILogger<FeedService> logger)
{
    /// <summary>
    /// <c>limit</c> khi client không gửi — hợp đồng ghi <c>default: 20</c>. Chỉ trang đầu với ĐÚNG giá trị này được cache
    /// (Đ-4.8): cache theo mọi <c>limit</c> là số khóa nổ theo tổ hợp, cho lợi ích gần bằng 0.
    /// </summary>
    public const int DefaultLimit = 20;

    /// <summary>Trần cứng (AGENTS.md Mục 9), cùng <c>ListUserPostsQuery.MaxLimit</c>.</summary>
    public const int MaxLimit = 50;

    public async Task<Result<FeedPage>> GetAsync(Guid me, string? rawCursor, int limit, CancellationToken ct)
    {
        // Validator đã kiểm; cursor hỏng lọt tới đây (chỉ khi gọi thẳng service) thì coi như trang đầu.
        PostCursor? cursor = PostCursor.TryDecode(rawCursor, out var decoded) ? decoded : null;

        // (1)(2)
        var sources = await sourceReader.GetAsync(me, ct);
        var mode = sources.IsEmpty ? FeedMode.Suggested : FeedMode.Network;
        var fingerprint = FeedFingerprint.Of(sources);
        var cacheable = cursor is null && limit == DefaultLimit;

        // (3)
        var cached = cacheable ? await pageCache.GetAsync(me, ct) : null;

        IReadOnlyList<Post> page;
        string? next;
        if (cached is not null && cached.Fingerprint == fingerprint && cached.Mode == mode)
        {
            // (5) trúng: nạp theo PK, sắp lại theo thứ tự đã cache. Bài đã xóa/ẩn trong 30s thì vắng mặt — trang ngắn hơn
            // limit là hợp lệ, next vẫn là cursor của danh sách gốc (Đ-4.9).
            var found = await posts.FindManyPublishedAsync(cached.Ids, ct);
            var byId = found.ToDictionary(p => p.PostId);
            page = [.. cached.Ids.Where(byId.ContainsKey).Select(id => byId[id])];
            next = cached.Next;
        }
        else
        {
            // (4)
            IReadOnlyList<Post> rows;
            try
            {
                rows = mode == FeedMode.Suggested
                    ? await store.SuggestedPageAsync(me, cursor, limit + 1, ct)
                    : await store.NetworkPageAsync(me, sources, cursor, limit + 1, ct);
            }
            catch (FeedQueryTimeoutException ex)
            {
                // Không id người dùng trong log (Mục 1.3 luật 9 của GĐ2): số nguồn đủ để đọc được vì sao chậm.
                logger.LogWarning(
                    ex, "Truy vấn feed vượt thời hạn ({Mode}, {SourceCount} nguồn) — trả 503",
                    mode, sources.Friends.Count + sources.FollowingOnly.Count);
                return ContentErrors.FeedUnavailable;
            }

            page = rows.Count > limit ? [.. rows.Take(limit)] : rows;

            // Cursor từ bài CUỐI của danh sách GỐC (trước bước 6): neo vào danh sách đã lọc là trang sau lặp lại hoặc bỏ sót
            // đúng những bài vừa bị lọc (cạm bẫy 1 của C4).
            next = rows.Count > limit ? new PostCursor(page[^1].CreatedAt, page[^1].PostId).Encode() : null;

            if (cacheable)
                await pageCache.SetAsync(
                    me, new CachedFeedPage([.. page.Select(p => p.PostId)], mode, next, fingerprint), ct);
        }

        // (6) Kiểm lại BR-02 (Đ-4.9) — cache không bao giờ là nguồn sự thật cuối.
        var visible = page
            .Where(p => mode == FeedMode.Suggested
                ? FeedVisibility.CanSeeSuggested(p.Privacy, p.Status, p.AuthorId, me)
                : FeedVisibility.CanSee(p.Privacy, p.Status, p.AuthorId, me, sources))
            .ToList();

        // (7)
        var items = await hydrator.HydrateAsync(visible, me, ct);
        return new FeedPage(items, next, mode);
    }
}
