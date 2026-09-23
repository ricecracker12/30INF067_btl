using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.SharedKernel.Authorization;

namespace SocialApp.IntegrationTests;

/// <summary>
/// C5: nguồn quyền thật đọc từ role_permissions trên Postgres thật đã migrate + seed. Kỳ vọng viết tay theo
/// Mục 5.3, CỐ Ý không đọc PermissionCodes/RoleCodes. Chỉ ĐỌC dữ liệu nền → dùng chung DB "authz" với matrix.
///
/// Đi qua DI của AddIdentityModule (RolePermissionSource là internal): test cả dòng đăng ký, không chỉ SQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RolePermissionSourceTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Mục 5.3 — USER: 11 quyền.
    private static readonly string[] UserPermissions =
    [
        "post.read.public", "post.read.friends", "post.create", "post.update", "post.delete",
        "comment.create", "reaction.set", "friend.request", "friend.respond", "message.send", "report.create",
    ];

    // MODERATOR = USER + post.hide + report.resolve.
    private static readonly string[] ModeratorPermissions = [.. UserPermissions, "post.hide", "report.resolve"];

    private ServiceProvider _services = null!;

    public async Task InitializeAsync() =>
        _services = new ServiceCollection()
            .AddIdentityModule(await postgres.SeededContentDatabaseAsync("authz"))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private async Task<IReadOnlySet<string>> GetAsync(string roleCode)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRolePermissionSource>().GetPermissionsAsync(roleCode);
    }

    [Fact]
    public async Task USER_dung_11_ma_quyen()
    {
        var permissions = await GetAsync("USER");

        Assert.Equal(UserPermissions.Order(StringComparer.Ordinal), permissions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MODERATOR_13_ma_co_post_hide_khong_co_user_lock()
    {
        var permissions = await GetAsync("MODERATOR");

        Assert.Equal(ModeratorPermissions.Order(StringComparer.Ordinal), permissions.Order(StringComparer.Ordinal));
        Assert.Contains("post.hide", permissions);
        Assert.DoesNotContain("user.lock", permissions);
    }

    /// <summary>ADMIN không có dòng role_permissions nào (thiết kế 3.2) — quyền đến từ short-circuit tầng 2.</summary>
    [Fact]
    public async Task ADMIN_tap_rong_dung_thiet_ke()
    {
        Assert.Empty(await GetAsync("ADMIN"));
    }

    [Theory]
    [InlineData("ROOT")]        // vai trò không tồn tại
    [InlineData("moderator")]   // role code phân biệt hoa thường — token phải mang đúng chuỗi hợp đồng
    public async Task Vai_tro_khong_khop_thi_tap_rong(string roleCode)
    {
        Assert.Empty(await GetAsync(roleCode));
    }
}
