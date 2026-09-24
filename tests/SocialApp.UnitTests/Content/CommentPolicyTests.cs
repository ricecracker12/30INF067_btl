using SocialApp.Modules.Content.Domain;
using Xunit;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// A1 GĐ3 — nội dung bình luận (FR-007, Đ-3.14) và độ sâu (BR-08, Đ-3.4) dưới dạng hàm thuần. Cùng vai với
/// <see cref="PostContentPolicyTests"/>: kiểm LUẬT; <c>CMT-03</c>/<c>CMT-05</c> ở integration kiểm luật đã nối vào endpoint.
/// </summary>
public sealed class CommentPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Body_rong_hoac_toan_khoang_trang_thi_khong_hop_le(string? body)
    {
        var result = CommentPolicy.Validate(body);

        Assert.False(result.IsValid);
        Assert.Equal(CommentPolicy.BodyKey, result.ErrorKey);
        Assert.Equal(CommentPolicy.Empty, result.Message);
    }

    [Fact]
    public void Dung_1000_ky_tu_hop_le_1001_thi_khong()
    {
        Assert.True(CommentPolicy.Validate(new string('a', CommentPolicy.MaxBodyLength)).IsValid);

        var result = CommentPolicy.Validate(new string('a', CommentPolicy.MaxBodyLength + 1));
        Assert.False(result.IsValid);
        Assert.Equal(CommentPolicy.BodyKey, result.ErrorKey);
        Assert.Equal(CommentPolicy.BodyTooLong, result.Message);
    }

    /// <summary>
    /// Đ-3.14: đếm UTF-16 như <c>string.Length</c> — một emoji ngoài BMP là HAI đơn vị. 500 emoji = 1000 đơn vị (hợp lệ),
    /// thêm một chữ nữa là vượt. FE đếm <c>.length</c> nên cùng con số.
    /// </summary>
    [Fact]
    public void Emoji_dem_theo_UTF16()
    {
        var fiveHundredEmoji = string.Concat(Enumerable.Repeat("😀", 500));

        Assert.Equal(1000, fiveHundredEmoji.Length);
        Assert.True(CommentPolicy.Validate(fiveHundredEmoji).IsValid);
        Assert.False(CommentPolicy.Validate(fiveHundredEmoji + "a").IsValid);
    }

    /// <summary>Không trim trước khi đo: khoảng trắng ở hai đầu vẫn là nội dung và vẫn tính vào độ dài.</summary>
    [Fact]
    public void Khong_trim_truoc_khi_do()
    {
        var body = " " + new string('a', CommentPolicy.MaxBodyLength - 1) + " ";

        Assert.False(CommentPolicy.Validate(body).IsValid);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void Tra_loi_cha_cap_1_2_thi_sau_hon_mot(short parentDepth, short expected)
    {
        var result = CommentDepthPolicy.ValidateReply(parentDepth);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Depth);
    }

    /// <summary>BR-08: cha đã ở cấp 3 thì không trả lời được — lỗi nằm dưới <c>parentId</c>, không phải <c>body</c>.</summary>
    [Fact]
    public void Tra_loi_cha_cap_3_thi_loi_BR08()
    {
        var result = CommentDepthPolicy.ValidateReply(CommentDepthPolicy.MaxDepth);

        Assert.False(result.IsValid);
        Assert.Null(result.Depth);
        Assert.Equal(CommentDepthPolicy.ParentIdKey, result.ErrorKey);
        Assert.Equal(CommentDepthPolicy.TooDeep, result.Message);
    }

    [Fact]
    public void Factory_tinh_depth_va_parent_khop_nhau()
    {
        var root = Comment.CreateRoot(Guid.NewGuid(), Guid.NewGuid(), "gốc");
        var level2 = Comment.CreateReply(root, Guid.NewGuid(), "cấp 2");
        var level3 = Comment.CreateReply(level2, Guid.NewGuid(), "cấp 3");

        Assert.Equal(1, root.Depth);
        Assert.Null(root.ParentId);
        Assert.Equal(2, level2.Depth);
        Assert.Equal(root.CommentId, level2.ParentId);
        Assert.Equal(3, level3.Depth);
        Assert.Equal(level2.CommentId, level3.ParentId);
        Assert.All([level2, level3], c => Assert.Equal(root.PostId, c.PostId));
        Assert.Throws<InvalidOperationException>(() => Comment.CreateReply(level3, Guid.NewGuid(), "cấp 4"));
    }
}
