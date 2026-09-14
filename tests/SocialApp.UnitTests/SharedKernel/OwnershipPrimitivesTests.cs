using System.Security.Claims;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Results;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>C6: hai viên gạch của khuôn tầng 3 — lỗi Forbidden dùng chung và danh tính người gọi từ claim sub.</summary>
public sealed class OwnershipPrimitivesTests
{
    [Fact]
    public void Forbidden_la_403_voi_mot_thong_diep_chung()
    {
        var result = Result.Forbidden();
        var typed = Result<Guid>.Forbidden();

        Assert.True(result.IsFailure);
        Assert.Equal(403, result.Error!.Value.Status);
        Assert.Equal("auth.forbidden", result.Error.Value.Code);
        Assert.Equal(result.Error, typed.Error);   // "không tồn tại" và "không phải của bạn" cùng MỘT lỗi
    }

    [Fact]
    public void GetUserId_doc_claim_sub()
    {
        var id = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "Bearer"));

        Assert.Equal(id, user.GetUserId());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("khong-phai-guid")]
    public void GetUserId_nem_khi_thieu_hoac_sai_sub(string? sub)
    {
        Claim[] claims = sub is null ? [] : [new Claim("sub", sub)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        Assert.Throws<InvalidOperationException>(() => user.GetUserId());
    }
}
