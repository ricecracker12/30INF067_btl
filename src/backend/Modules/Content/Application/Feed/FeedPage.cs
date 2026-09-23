using SocialApp.Modules.Content.Application.Posts;

namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Body 200 của <c>GET /feed</c> (giai-doan-4.md Mục 8.2). <c>Items</c> dùng lại đúng <see cref="PostResponse"/> — GĐ3 thêm
/// <c>myReaction</c> thì feed có trường đó qua <see cref="PostHydrator"/>, không sửa dòng nào ở đây.
/// </summary>
/// <param name="NextCursor">
/// <c>Items</c> có thể ít hơn <c>limit</c> (bước kiểm lại BR-02 của Đ-4.9 loại bài không còn được thấy). Hết dữ liệu khi và
/// chỉ khi <c>NextCursor</c> là <c>null</c> — không phải chuỗi rỗng.
/// </param>
public sealed record FeedPage(IReadOnlyList<PostResponse> Items, string? NextCursor, FeedMode Mode);
