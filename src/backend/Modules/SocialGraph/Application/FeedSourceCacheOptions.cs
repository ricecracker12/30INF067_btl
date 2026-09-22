namespace SocialApp.Modules.SocialGraph.Application;

/// <summary>
/// Công tắc cache nguồn feed (Đ-4.8). Chỉ <see cref="Enabled"/> là cấu hình; TTL 60s là hằng vì đã chốt.
/// Mặc định bật. Bind có điều kiện (Q-C2): có <c>IConfiguration</c> thì đọc section, không thì giữ mặc định —
/// <c>ServiceCollection</c> trần của test không phải dựng host chỉ để resolve reader.
/// </summary>
public sealed class FeedSourceCacheOptions
{
    public const string Section = "Feed:SourceCache";

    public bool Enabled { get; set; } = true;
}
