namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Con trỏ của danh sách BÀI (Đ-2.11), sắp <c>(created_at DESC, post_id DESC)</c>. Từ D0 GĐ3 là lớp mỏng trên
/// <see cref="KeysetCursor"/> — bộ mã hóa dùng chung với danh sách bình luận (Đ-3.6). Giữ lại kiểu riêng để chữ ký của
/// <c>IPostStore</c>/<c>IFeedStore</c> nói rõ "vị trí của một BÀI", và để cursor bài đã phát ra trước GĐ3 vẫn giải mã được:
/// định dạng trên dây y hệt.
/// </summary>
public readonly record struct PostCursor(DateTimeOffset CreatedAt, Guid PostId)
{
    public string Encode() => new KeysetCursor(CreatedAt, PostId).Encode();

    /// <summary>Không ném với bất kỳ đầu vào nào — xem <see cref="KeysetCursor.TryDecode"/>.</summary>
    public static bool TryDecode(string? raw, out PostCursor cursor)
    {
        var ok = KeysetCursor.TryDecode(raw, out var keyset);
        cursor = ok ? new PostCursor(keyset.CreatedAt, keyset.Id) : default;
        return ok;
    }
}
