namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Giá trị cache trang đầu <c>feed:p1:{userId}</c> — đúng bốn trường (Đ-4.8 sửa 2026-09-22, Đ-4.9). <b>Không</b> có
/// <c>PostResponse</c>, URL đã ký hay bất kỳ trường theo người xem nào: cache chỉ lưu id, hydrate luôn chạy lúc trả.
/// </summary>
/// <param name="Ids">Id các bài của trang, ≤ <c>limit</c>, theo thứ tự hiển thị — danh sách GỐC, trước bước kiểm lại.</param>
/// <param name="Next"><c>nextCursor</c> đã mã hóa, tính từ danh sách gốc; lưu sẵn vì bài thứ <c>limit</c> có thể đã bị xóa.</param>
/// <param name="Fingerprint">Dấu nguồn (Q-C1): khác dấu của nguồn hiện tại → coi như trượt.</param>
public sealed record CachedFeedPage(IReadOnlyList<Guid> Ids, FeedMode Mode, string? Next, string Fingerprint);

/// <summary>
/// Cache trang đầu của feed (Đ-4.8). Hiện thực ở <c>Infrastructure</c> (Redis) — <c>Application</c> không chạm
/// StackExchange.Redis (luật 7). <b>Fail-open</b> ở cả ba thao tác: Redis lỗi thì đọc trả <c>null</c>, ghi/xóa nuốt lỗi và
/// log — thiếu cache chỉ chậm hơn, còn ném ra là mất feed. Công tắc <c>Feed:PageCache:Enabled</c> cũng kiểm bên trong
/// hiện thực: tắt thì đọc luôn <c>null</c>, không ghi.
/// </summary>
public interface IFeedPageCache
{
    Task<CachedFeedPage?> GetAsync(Guid userId, CancellationToken ct);

    Task SetAsync(Guid userId, CachedFeedPage page, CancellationToken ct);

    /// <summary>
    /// Xóa khóa của TÁC GIẢ sau khi bài của chính họ đã lưu (đăng/sửa/xóa) — với tác giả, 30s trông như "đăng bài không
    /// lên". Người gọi truyền <see cref="CancellationToken.None"/>: bài đã COMMIT thì client ngắt không được bỏ dở việc xóa.
    /// </summary>
    Task InvalidateAsync(Guid authorId, CancellationToken ct);
}
