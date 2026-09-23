using System.Web;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// C2 (GĐ2), Mục 10.2 mức 1 — kiểm chữ ký presign THUẦN, không chạm mạng: SigV4 là HMAC cục bộ nên URL dựng được với khóa
/// giả. Bốn nhóm theo hướng dẫn khối C: path-style, hạn PUT/GET khác nhau, và quan trọng nhất — <c>content-length</c> +
/// <c>content-type</c> NẰM TRONG <c>X-Amz-SignedHeaders</c> của URL PUT (Đ-2.8 lớp 1). Ba nhóm đầu đẹp; nhóm cuối là nhóm duy
/// nhất bắt được lỗi đắt nhất (ký cho 2MB rồi client PUT 400MB vẫn vào bucket).
///
/// Kiểm chứng CORS + PUT thật lên bucket <c>-dev</c> từ trình duyệt là việc kiểm tay có bằng chứng (Mục 10.2 mức 3) — không
/// có test nào ở đây thay được nó.
/// </summary>
public sealed class R2ObjectStorageTests
{
    private static readonly R2Options Options = new()
    {
        Endpoint = "https://test-account.r2.cloudflarestorage.com",
        Bucket = "socialapp-test",
        AccessKey = "test-access-key",
        SecretKey = "test-secret-key",
    };

    private static (Uri Url, System.Collections.Specialized.NameValueCollection Query) PresignPut(long length = 1_048_576)
    {
        using var storage = new R2ObjectStorage(Options);
        var url = new Uri(storage.CreatePresignedPut("posts/u/a.jpg", "image/jpeg", length));
        return (url, HttpUtility.ParseQueryString(url.Query));
    }

    [Fact]
    public void PUT_dung_path_style_tren_endpoint_R2_khong_phai_virtual_host()
    {
        var (url, _) = PresignPut();

        Assert.Equal("test-account.r2.cloudflarestorage.com", url.Host);
        Assert.StartsWith("/socialapp-test/posts/u/a.jpg", url.AbsolutePath);
    }

    [Fact]
    public void PUT_ky_SigV4_han_10_phut()
    {
        var (_, q) = PresignPut();

        Assert.Equal("AWS4-HMAC-SHA256", q["X-Amz-Algorithm"]);
        Assert.Equal("600", q["X-Amz-Expires"]);
        Assert.NotNull(q["X-Amz-Signature"]);
    }

    [Fact]
    public void PUT_co_content_length_va_content_type_trong_signed_headers()
    {
        // Đ-2.8 lớp 1 — nhóm test duy nhất bắt lỗi đắt nhất. Thiếu content-length là PUT 400MB vẫn qua.
        var (_, q) = PresignPut();
        var signed = (q["X-Amz-SignedHeaders"] ?? "").Split(';');

        Assert.Contains("content-length", signed);
        Assert.Contains("content-type", signed);
        Assert.Contains("host", signed);
    }

    [Fact]
    public void GET_han_15_phut_khac_PUT_va_khong_ky_content_headers()
    {
        using var storage = new R2ObjectStorage(Options);
        var url = new Uri(storage.CreatePresignedGet("posts/u/a.jpg"));
        var q = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("900", q["X-Amz-Expires"]);
        Assert.Equal("host", q["X-Amz-SignedHeaders"]);
    }

    [Fact]
    public void Thieu_cau_hinh_thi_khong_dung_duoc()
    {
        var incomplete = new R2Options { Endpoint = Options.Endpoint, Bucket = Options.Bucket };

        Assert.Throws<ArgumentException>(() => new R2ObjectStorage(incomplete));
    }
}
