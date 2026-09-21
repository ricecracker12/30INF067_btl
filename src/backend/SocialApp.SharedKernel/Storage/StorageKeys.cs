using SocialApp.SharedKernel.Ids;

namespace SocialApp.SharedKernel.Storage;

/// <summary>
/// Dạng key của object trên R2 (Đ-2.7): <c>posts/{userId}/{uuid7}.{ext}</c> và <c>avatars/{userId}/{uuid7}.{ext}</c>.
/// Tiền tố người dùng KHÔNG phải trang trí — nó là thứ D5 kiểm để chặn IDOR "A gắn ảnh của B vào bài của mình"
/// (<c>TC-A03-media</c>): mọi key trong body phải <see cref="BelongsTo"/> người gọi.
///
/// <c>ext</c> suy từ <c>contentType</c> theo allowlist, KHÔNG lấy từ tên file client gửi lên — tên file là dữ liệu
/// client, và <c>.jpg.exe</c> là một trò cũ. Allowlist ở đây phải là tập con của <c>MediaAttachment.AllowedContentTypes</c>
/// (CHECK ở DB, Đ-2.8 lớp 3) — unit test <c>StorageKeysTests</c> canh hai danh sách không lệch nhau.
/// </summary>
public static class StorageKeys
{
    public const string PostsPrefix = "posts";
    public const string AvatarsPrefix = "avatars";

    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    /// <summary>Ba loại ảnh được nhận (Đ-2.8), đúng bằng allowlist của <c>ck_media_content_type</c>.</summary>
    public static IReadOnlyCollection<string> AllowedContentTypes => ExtensionByContentType.Keys;

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && ExtensionByContentType.ContainsKey(contentType);

    /// <summary>Key mới cho ảnh bài của <paramref name="userId"/>. Ném nếu <paramref name="contentType"/> ngoài allowlist.</summary>
    public static string ForPost(Guid userId, string contentType) => Build(PostsPrefix, userId, contentType);

    /// <summary>Key mới cho avatar của <paramref name="userId"/>. Ném nếu <paramref name="contentType"/> ngoài allowlist.</summary>
    public static string ForAvatar(Guid userId, string contentType) => Build(AvatarsPrefix, userId, contentType);

    /// <summary>
    /// Key có nằm dưới <c>{prefix}/{userId}/</c> không — kiểm tiền tố của Đ-2.7. So sánh Ordinal trên chuỗi đã chuẩn hóa
    /// <c>Guid.ToString("D")</c>, nên <c>userId</c> viết hoa/thường trong key đều không khớp: key do chính
    /// <see cref="ForPost"/>/<see cref="ForAvatar"/> sinh ra thì luôn đúng dạng, key "gõ tay" thì không được tin.
    /// </summary>
    public static bool BelongsTo(string key, string prefix, Guid userId) =>
        key.StartsWith($"{prefix}/{userId:D}/", StringComparison.Ordinal);

    private static string Build(string prefix, Guid userId, string contentType)
    {
        if (!ExtensionByContentType.TryGetValue(contentType, out var ext))
            throw new ArgumentException($"Loại ảnh không được nhận: '{contentType}'. Chỉ nhận {string.Join(", ", AllowedContentTypes)}.", nameof(contentType));

        return $"{prefix}/{userId:D}/{Uuid7.New():N}.{ext}";
    }
}
