using SocialApp.Modules.Content.Domain;
using Xunit;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// BR-01 (A4, GĐ2 khối A). Ba ca đầu là AC-02, AC-03 và BR01-06 của Mục 10.1 — cùng kịch bản với ba test
/// integration của khối D, nhưng ở đây không cần Postgres vì <see cref="PostContentPolicy"/> là hàm thuần.
///
/// Test này KHÔNG thay được ba ca integration kia: nó kiểm luật, còn chúng kiểm luật ĐÃ ĐƯỢC NỐI vào
/// endpoint và ánh xạ đúng sang <c>errors</c> của Problem Details.
/// </summary>
public sealed class PostContentPolicyTests
{
    /// <summary>AC-02 — body rỗng và không ảnh thì không hợp lệ, lỗi nằm dưới trường <c>body</c>.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Body_rong_va_khong_anh_thi_khong_hop_le(string? body)
    {
        var result = PostContentPolicy.Validate(body, mediaCount: 0);

        Assert.False(result.IsValid);
        Assert.Equal(PostContentPolicy.BodyKey, result.ErrorKey);
        Assert.Equal(PostContentPolicy.Empty, result.Message);
    }

    /// <summary>AC-03 — quá 10 ảnh thì không hợp lệ, lỗi nằm dưới trường <c>mediaKeys</c>.</summary>
    [Fact]
    public void Muoi_mot_anh_thi_khong_hop_le()
    {
        var result = PostContentPolicy.Validate("có chữ hẳn hoi", mediaCount: PostContentPolicy.MaxMediaCount + 1);

        Assert.False(result.IsValid);
        Assert.Equal(PostContentPolicy.MediaKeysKey, result.ErrorKey);
        Assert.Equal(PostContentPolicy.TooManyMedia, result.Message);
    }

    /// <summary>
    /// BR01-06 — bài chỉ có ảnh, không có chữ, là HỢP LỆ. Đây là ca dễ hỏng nhất: để <c>Body</c> thành
    /// <c>required string</c> hay bắt buộc không rỗng thì cả luồng đăng ảnh của UC-04 chết.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Chi_co_anh_khong_co_chu_thi_hop_le(string? body)
    {
        var result = PostContentPolicy.Validate(body, mediaCount: 1);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorKey);
        Assert.Null(result.Message);
    }

    /// <summary>Biên của hai mệnh đề đếm được: đúng 10 ảnh và đúng 5000 ký tự vẫn hợp lệ.</summary>
    [Fact]
    public void Dung_bien_thi_van_hop_le()
    {
        Assert.True(PostContentPolicy.Validate(null, PostContentPolicy.MaxMediaCount).IsValid);
        Assert.True(PostContentPolicy.Validate(new string('a', PostContentPolicy.MaxBodyLength), 0).IsValid);
    }

    /// <summary>Quá 5000 ký tự thì lỗi nằm dưới <c>body</c> — kể cả khi bài có ảnh.</summary>
    [Fact]
    public void Body_qua_dai_thi_khong_hop_le()
    {
        var result = PostContentPolicy.Validate(new string('a', PostContentPolicy.MaxBodyLength + 1), mediaCount: 3);

        Assert.False(result.IsValid);
        Assert.Equal(PostContentPolicy.BodyKey, result.ErrorKey);
        Assert.Equal(PostContentPolicy.BodyTooLong, result.Message);
    }

    /// <summary>
    /// Số ảnh ÂM là lỗi lập trình ở tầng gọi, nhưng phải rơi vào <c>mediaKeys</c> chứ không lọt qua mệnh đề
    /// 1 rồi bị mệnh đề 3 gán nhầm cho <c>body</c> — chỗ đó là một giờ dò tìm sai hướng.
    /// </summary>
    [Fact]
    public void So_anh_am_thi_bao_loi_o_mediaKeys()
    {
        var result = PostContentPolicy.Validate("có chữ hẳn hoi", mediaCount: -1);

        Assert.False(result.IsValid);
        Assert.Equal(PostContentPolicy.MediaKeysKey, result.ErrorKey);
    }

    /// <summary>
    /// Bài chỉ có chữ, không ảnh, là hợp lệ — mệnh đề 3 nhìn <c>btrim</c>, và một chữ cũng đủ.
    /// </summary>
    [Fact]
    public void Chi_co_chu_khong_anh_thi_hop_le()
    {
        Assert.True(PostContentPolicy.Validate("a", mediaCount: 0).IsValid);
    }
}
