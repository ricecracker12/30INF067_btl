using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Authorization;
using Xunit;

namespace SocialApp.UnitTests.Authorization;

/// <summary>Đ1: chuỗi "ADMIN" là hợp đồng, và Identity trỏ về CÙNG một hằng số với short-circuit tầng 2.</summary>
public sealed class SystemRolesTests
{
    [Fact]
    public void Ma_ADMIN_la_hop_dong_va_Identity_tro_ve_cung_mot_hang_so()
    {
        Assert.Equal("ADMIN", SystemRoles.Admin);            // viết tay — đổi chuỗi là phá mọi token đang lưu hành
        Assert.Equal(SystemRoles.Admin, RoleCodes.Admin);
    }
}
