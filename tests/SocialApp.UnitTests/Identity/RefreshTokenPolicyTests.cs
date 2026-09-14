using SocialApp.Modules.Identity.Domain;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>Đ-D3 / Mục 7.3: ân hạn 10 giây cho race hai tab. Giá trị viết tay.</summary>
public sealed class RefreshTokenPolicyTests
{
    [Fact]
    public void An_han_reuse_10_giay()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), RefreshTokenPolicy.ReuseGracePeriod);
    }
}
