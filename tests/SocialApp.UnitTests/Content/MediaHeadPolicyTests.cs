using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// C3 (GĐ2): Đ-2.8 lớp 2 — ba nhánh hỏng, một nhánh hợp lệ. Không mạng, không fake: chỉ hai record. Kỳ vọng thông điệp
/// viết theo hằng số của policy vì D5 đặt nguyên câu đó vào <c>errors.mediaKeys</c> — FE hiện đúng câu server (luật FE Mục 6).
/// </summary>
public sealed class MediaHeadPolicyTests
{
    private static readonly MediaDeclaration Declared = new("image/jpeg", 1_048_576);

    [Fact]
    public void Object_chua_co_thi_bao_chua_tai_len_xong()
    {
        var result = MediaHeadPolicy.Check(Declared, actual: null);

        Assert.False(result.IsValid);
        Assert.Equal("mediaKeys", result.ErrorKey);
        Assert.Equal(MediaHeadPolicy.NotUploaded, result.Message);
    }

    [Fact]
    public void Khai_1MB_nhung_object_that_12MB_thi_lech_dung_luong()
    {
        var actual = new ObjectHead(12L * 1024 * 1024, "image/jpeg", DateTimeOffset.UtcNow);

        var result = MediaHeadPolicy.Check(Declared, actual);

        Assert.Equal("mediaKeys", result.ErrorKey);
        Assert.Equal(MediaHeadPolicy.SizeMismatch, result.Message);
    }

    [Fact]
    public void Khai_jpeg_nhung_object_that_png_thi_lech_loai()
    {
        var actual = new ObjectHead(Declared.SizeBytes, "image/png", DateTimeOffset.UtcNow);

        var result = MediaHeadPolicy.Check(Declared, actual);

        Assert.Equal("mediaKeys", result.ErrorKey);
        Assert.Equal(MediaHeadPolicy.TypeMismatch, result.Message);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("Image/JPEG")]   // R2 trả đúng chuỗi client đặt lúc PUT; hoa thường không phải khác loại
    public void Khop_ca_dung_luong_lan_loai_thi_hop_le(string contentTypeFromHead)
    {
        var actual = new ObjectHead(Declared.SizeBytes, contentTypeFromHead, DateTimeOffset.UtcNow);

        var result = MediaHeadPolicy.Check(Declared, actual);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorKey);
    }

    [Fact]
    public void Object_chua_co_thi_khong_xet_toi_dung_luong_hay_loai()
    {
        // Thứ tự nhánh: null → dung lượng → loại. Object chưa có mà bảo "lệch dung lượng" là chỉ sai chỗ sửa.
        var result = MediaHeadPolicy.Check(new MediaDeclaration("image/png", 5), actual: null);

        Assert.Equal(MediaHeadPolicy.NotUploaded, result.Message);
    }
}
