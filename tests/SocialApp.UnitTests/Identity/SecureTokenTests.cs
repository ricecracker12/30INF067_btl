using System.Security.Cryptography;
using System.Text;
using SocialApp.Modules.Identity.Application.Security;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Đ-D2: một helper cho cả token xác minh email lẫn refresh token. Kỳ vọng viết tay — so Hash với chính Hash thì
/// đổi thuật toán kiểu gì test cũng xanh.
/// </summary>
public sealed class SecureTokenTests
{
    [Fact]
    public void Generate_64_ky_tu_hex_thuong()
    {
        var token = SecureToken.Generate();

        Assert.Equal(64, token.Length);
        Assert.Matches("^[0-9a-f]{64}$", token);
    }

    [Fact]
    public void Generate_hai_lan_khac_nhau()
    {
        Assert.NotEqual(SecureToken.Generate(), SecureToken.Generate());
    }

    [Fact]
    public void Hash_abc_dung_vector_SHA256_chuan()
    {
        // FIPS 180-2, vector "abc" — viết tay, không tính lại bằng code.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SecureToken.Hash("abc"));
    }

    /// <summary>Bẫy Đ-D2: băm 32 byte gốc (giải hex) thay vì byte UTF-8 của chuỗi → mọi link xác minh 400.</summary>
    [Fact]
    public void Hash_bam_byte_UTF8_cua_chuoi_khong_bam_byte_goc()
    {
        var token = SecureToken.Generate();

        var ofUtf8 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var ofDecodedBytes = Convert.ToHexString(SHA256.HashData(Convert.FromHexString(token))).ToLowerInvariant();

        Assert.Equal(ofUtf8, SecureToken.Hash(token));
        Assert.NotEqual(ofDecodedBytes, SecureToken.Hash(token));
    }
}
