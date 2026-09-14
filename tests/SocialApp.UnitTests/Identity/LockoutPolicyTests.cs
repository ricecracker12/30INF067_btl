using SocialApp.Modules.Identity.Domain;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>FR-003 đã chốt: 5 lần sai liên tiếp → khóa 15 phút. Giá trị viết tay.</summary>
public sealed class LockoutPolicyTests
{
    [Fact]
    public void Nguong_5_lan_khoa_15_phut()
    {
        Assert.Equal(5, LockoutPolicy.MaxFailedAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), LockoutPolicy.LockDuration);
    }
}
