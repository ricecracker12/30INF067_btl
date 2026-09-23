using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Profile;

/// <summary>
/// D3 — regex <c>mediaKey</c> của <see cref="SetAvatarRequestValidator"/>. Ở tầng unit vì đây là hàm thuần: mười ca biên
/// ở đây rẻ hơn mười lượt HTTP, và integration chỉ cần một ca chứng minh validator được nối vào đường request.
/// </summary>
public sealed class SetAvatarRequestValidatorTests
{
    private static readonly SetAvatarRequestValidator Validator = new();

    private static bool IsValid(string mediaKey) =>
        Validator.Validate(new SetAvatarRequest { MediaKey = mediaKey }).IsValid;

    /// <summary>
    /// Key do chính <see cref="StorageKeys.ForAvatar"/> sinh ra phải LUÔN qua được regex. Đây là khẳng định quan trọng
    /// nhất của lớp này: regex ở hợp đồng và hàm sinh key ở SharedKernel là hai nguồn độc lập, lệch nhau thì luồng hợp lệ
    /// đứt ở bước cuối mà không có gì báo trước.
    /// </summary>
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Key_do_StorageKeys_sinh_ra_luon_hop_le(string contentType) =>
        Assert.True(IsValid(StorageKeys.ForAvatar(Guid.NewGuid(), contentType)));

    /// <summary>Các cách sai dạng. Mỗi dòng là một cách hỏng khác nhau, không phải biến thể của cùng một cách.</summary>
    [Theory]
    [InlineData("")]                                                                  // thiếu trường
    [InlineData("avatars/abc.jpg")]                                                   // thiếu hẳn một tầng
    [InlineData("posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.jpg")]   // sai tiền tố loại
    [InlineData("avatars/khong-phai-guid/0123456789abcdef0123456789abcdef.jpg")]      // id không phải Guid
    [InlineData("avatars/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.gif")] // đuôi ngoài allowlist
    [InlineData("avatars/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789ABCDEF0123456789abcdef.jpg")] // hex viết hoa
    [InlineData("avatars/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef.jpg")] // phần tên quá ngắn
    [InlineData("avatars/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.jpg/x")] // có đuôi thừa
    public void Key_sai_dang_bi_chan(string mediaKey) =>
        Assert.False(IsValid(mediaKey));

    /// <summary>
    /// Regex phải neo hai đầu. Không neo thì <c>"rác/" + key hợp lệ</c> lọt qua lớp 1, và lớp 2
    /// (<c>StorageKeys.BelongsTo</c> dùng <c>StartsWith</c>) cũng không bắt được vì nó chỉ nhìn phần đầu.
    /// </summary>
    [Fact]
    public void Regex_neo_hai_dau()
    {
        var key = StorageKeys.ForAvatar(Guid.NewGuid(), "image/jpeg");

        Assert.False(IsValid("rác/" + key));
        Assert.False(IsValid(key + " "));
        Assert.False(IsValid("\n" + key));
    }
}
