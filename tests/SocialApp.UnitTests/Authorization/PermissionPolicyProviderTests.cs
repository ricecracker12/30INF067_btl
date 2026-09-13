using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;
using SocialApp.SharedKernel.Authorization;
using Xunit;

namespace SocialApp.UnitTests.Authorization;

/// <summary>
/// C1: <see cref="PermissionPolicyProvider"/> dựng policy <c>perm:*</c> và rơi về provider mặc định cho mọi
/// thứ khác. Khẳng định (3) tồn tại để bắt đúng lỗi câm: provider tự trả null ở GetFallbackPolicyAsync thì
/// default deny của C4 biến mất mà không có lỗi nào khác báo.
/// </summary>
public sealed class PermissionPolicyProviderTests
{
    [Fact]
    public void Attribute_sinh_ten_policy_bang_tien_to_cong_ma_quyen()
    {
        var attribute = new RequirePermissionAttribute("post.hide");

        Assert.Equal("perm:post.hide", attribute.Policy);
        Assert.Equal("post.hide", attribute.Permission);
    }

    [Fact]
    public async Task Policy_perm_dung_mot_PermissionRequirement_va_yeu_cau_da_dang_nhap()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync("perm:post.hide");

        Assert.NotNull(policy);
        var permission = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("post.hide", permission.Permission);
        Assert.Single(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
        Assert.Equal(2, policy.Requirements.Count);
    }

    [Fact]
    public async Task Ten_policy_khong_co_tien_to_roi_ve_provider_mac_dinh()
    {
        var named = new AuthorizationPolicyBuilder().RequireRole("SOMETHING").Build();
        var authorization = new AuthorizationOptions();
        authorization.AddPolicy("named", named);
        var options = Options.Create(authorization);

        var provider = new PermissionPolicyProvider(options);
        var fallback = new DefaultAuthorizationPolicyProvider(options);

        Assert.Same(await fallback.GetPolicyAsync("named"), await provider.GetPolicyAsync("named"));
        Assert.Same(named, await provider.GetPolicyAsync("named"));
        Assert.Null(await provider.GetPolicyAsync("khong-ton-tai"));
    }

    [Fact]
    public async Task Fallback_policy_va_default_policy_van_doc_duoc_qua_provider_moi()
    {
        var fallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var defaultPolicy = new AuthorizationPolicyBuilder().RequireClaim("sub").Build();
        var options = Options.Create(new AuthorizationOptions
        {
            FallbackPolicy = fallbackPolicy,
            DefaultPolicy = defaultPolicy,
        });

        var provider = new PermissionPolicyProvider(options);

        Assert.Same(fallbackPolicy, await provider.GetFallbackPolicyAsync());
        Assert.Same(defaultPolicy, await provider.GetDefaultPolicyAsync());
    }
}
