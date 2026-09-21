using System.Text.RegularExpressions;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>C2 (GĐ2): dạng key Đ-2.7, allowlist Đ-2.8, và kiểm tiền tố mà D5 dùng cho <c>TC-A03-media</c>.</summary>
public sealed class StorageKeysTests
{
    private static readonly Regex KeyShape =
        new(@"^(posts|avatars)/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/[0-9a-f]{32}\.(jpg|png|webp)$");

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("Image/JPEG", "jpg")]   // hoa thường không phải khác loại
    public void Key_bai_dung_dang_posts_userId_uuid7_ext(string contentType, string ext)
    {
        var userId = Guid.NewGuid();

        var key = StorageKeys.ForPost(userId, contentType);

        Assert.Matches(KeyShape, key);
        Assert.StartsWith($"posts/{userId:D}/", key);
        Assert.EndsWith($".{ext}", key);
    }

    [Fact]
    public void Key_avatar_dung_tien_to_avatars()
    {
        var userId = Guid.NewGuid();
        var key = StorageKeys.ForAvatar(userId, "image/png");

        Assert.Matches(KeyShape, key);
        Assert.StartsWith($"avatars/{userId:D}/", key);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public void Loai_ngoai_allowlist_bi_tu_choi(string contentType)
    {
        Assert.False(StorageKeys.IsAllowedContentType(contentType));
        Assert.Throws<ArgumentException>(() => StorageKeys.ForPost(Guid.NewGuid(), contentType));
    }

    [Fact]
    public void Allowlist_trung_voi_CHECK_o_DB()
    {
        // Hai danh sách ở hai chỗ (SharedKernel không nhìn thấy module Content) — test này là thứ giữ chúng không lệch.
        Assert.Equal(
            MediaAttachment.AllowedContentTypes.Order().ToArray(),
            StorageKeys.AllowedContentTypes.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void BelongsTo_chi_dung_voi_dung_nguoi_dung_dung_tien_to()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var keyOfB = StorageKeys.ForPost(b, "image/jpeg");

        Assert.True(StorageKeys.BelongsTo(keyOfB, StorageKeys.PostsPrefix, b));
        Assert.False(StorageKeys.BelongsTo(keyOfB, StorageKeys.PostsPrefix, a));          // IDOR — TC-A03-media
        Assert.False(StorageKeys.BelongsTo(keyOfB, StorageKeys.AvatarsPrefix, b));        // sai tiền tố
        Assert.False(StorageKeys.BelongsTo($"posts/{b:D}", StorageKeys.PostsPrefix, b));  // thiếu dấu / — không phải key thật
    }
}
