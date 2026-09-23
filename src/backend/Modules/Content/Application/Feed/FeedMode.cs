namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Trường <c>mode</c> của <see cref="FeedPage"/> (Đ-4.6). Ra dây <c>"network"</c> / <c>"suggested"</c> nhờ converter
/// CamelCase của host. FE dựa vào nó để hiện nhãn "Gợi ý cho bạn" — không có nó thì FE phải tự dựng lại luật của server.
/// </summary>
public enum FeedMode
{
    /// <summary>Có ít nhất một kết nối (bạn hoặc đang theo dõi) — kể cả khi feed rỗng.</summary>
    Network,

    /// <summary>Chưa có kết nối nào: bài công khai mới nhất của người khác, cộng bài của chính mình (Đ-4.6 sửa 2026-09-23).</summary>
    Suggested,
}
