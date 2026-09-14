using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SocialApp.SharedKernel.Authorization;
using Xunit;

namespace SocialApp.UnitTests.Authorization;

/// <summary>
/// C2: logic handler cô lập khỏi DB — nhờ vậy matrix đỏ thì không mơ hồ giữa "handler sai" và "SQL sai" (Đ3).
/// Cache giả CHỈ sống ở unit test. Vai trò và tên claim viết tay.
/// </summary>
public sealed class PermissionHandlerTests
{
    private static AuthorizationHandlerContext Context(string? role, string permission = "post.hide")
    {
        Claim[] claims = role is null ? [new Claim("sub", "u1")] : [new Claim("sub", "u1"), new Claim("role", role)];
        return new AuthorizationHandlerContext(
            [new PermissionRequirement(permission)],
            new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test")),
            resource: null);
    }

    [Fact]
    public async Task Admin_qua_ma_khong_cham_cache()
    {
        var ctx = Context("ADMIN");

        await new PermissionHandler(new ThrowingCache()).HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Moderator_co_quyen_thi_qua()
    {
        var ctx = Context("MODERATOR");

        await new PermissionHandler(new FixedCache("post.hide")).HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task User_thieu_quyen_khong_qua_va_khong_Fail()
    {
        var ctx = Context("USER");

        await new PermissionHandler(new FixedCache("post.create")).HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
        Assert.False(ctx.HasFailed);   // không chặn nhầm handler khác cùng requirement
    }

    [Fact]
    public async Task Thieu_claim_role_khong_qua()
    {
        var ctx = Context(role: null);

        await new PermissionHandler(new ThrowingCache()).HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task admin_chu_thuong_khong_duoc_short_circuit()
    {
        var ctx = Context("admin");

        await new PermissionHandler(new FixedCache()).HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
    }

    private sealed class ThrowingCache : IPermissionCache
    {
        public ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default) =>
            throw new InvalidOperationException($"Không được chạm cache cho vai trò '{roleCode}'.");
    }

    private sealed class FixedCache(params string[] permissions) : IPermissionCache
    {
        public ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlySet<string>>(permissions.ToHashSet(StringComparer.Ordinal));
    }
}
