using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.Identity.Application.Security;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>NFR-SEC-01: BCrypt cost 12. Cost đọc từ chính chuỗi hash, số 12 viết tay.</summary>
public sealed class BCryptPasswordHasherTests
{
    private const string Password = "MatKhauManh123";

    private readonly IPasswordHasher _hasher = IdentityServices.Build().GetRequiredService<IPasswordHasher>();

    [Fact]
    public void Hash_la_BCrypt_cost_12_va_khac_mat_khau()
    {
        var hash = _hasher.Hash(Password);

        Assert.StartsWith("$2", hash);
        Assert.Equal("12", hash.Split('$')[2]);   // "$2a$12$<salt+hash>" → ["", "2a", "12", ...]
        Assert.NotEqual(Password, hash);
    }

    [Fact]
    public void Hai_lan_bam_cung_mat_khau_ra_hai_hash_khac_nhau()
    {
        Assert.NotEqual(_hasher.Hash(Password), _hasher.Hash(Password));   // salt ngẫu nhiên
    }

    [Fact]
    public void Verify_dung_mat_khau_true_sai_mat_khau_false()
    {
        var hash = _hasher.Hash(Password);

        Assert.True(_hasher.Verify(Password, hash));
        Assert.False(_hasher.Verify(Password + "x", hash));
    }

    /// <summary>
    /// AC-02 qua thời gian phản hồi: nhánh email không tồn tại phải tốn CÙNG chi phí với Verify thật. Hash giả cost 10
    /// nhanh hơn 4 lần (tỉ lệ ~0,25); ngưỡng 0,5 đủ xa để không chập chờn. So thời gian NHỎ NHẤT của vài lần đo —
    /// nhiễu của máy chỉ làm chậm đi, không làm nhanh lên.
    /// </summary>
    [Fact]
    public void VerifyAgainstDummy_ton_chi_phi_ngang_Verify_that()
    {
        var hash = _hasher.Hash(Password);
        _hasher.VerifyAgainstDummy(Password);   // khởi tạo hash giả (Lazy) ngoài phần đo

        var real = MinElapsed(() => _hasher.Verify(Password, hash));
        var dummy = MinElapsed(() => _hasher.VerifyAgainstDummy(Password));

        Assert.True(dummy.TotalMilliseconds >= real.TotalMilliseconds * 0.5,
            $"VerifyAgainstDummy {dummy.TotalMilliseconds:F0} ms, Verify thật {real.TotalMilliseconds:F0} ms — hash giả rẻ hơn hash thật.");
    }

    private static TimeSpan MinElapsed(Action action)
    {
        var min = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            if (sw.Elapsed < min)
                min = sw.Elapsed;
        }
        return min;
    }
}
