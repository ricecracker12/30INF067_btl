namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Công tắc cache trang đầu (Đ-4.8). Chỉ <see cref="Enabled"/> là cấu hình; TTL 30s là hằng vì đã chốt. Mặc định bật; k6
/// chạy một lượt với cache tắt để có con số lạnh (Đ-4.13). Bind có điều kiện (Q-C2) — cùng khuôn
/// <c>FeedSourceCacheOptions</c> của SocialGraph.
/// </summary>
public sealed class FeedPageCacheOptions
{
    public const string Section = "Feed:PageCache";

    public bool Enabled { get; set; } = true;
}
