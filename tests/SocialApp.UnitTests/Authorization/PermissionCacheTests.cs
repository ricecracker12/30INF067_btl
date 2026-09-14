using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Authorization;
using Xunit;

namespace SocialApp.UnitTests.Authorization;

/// <summary>
/// C3: TTL 60 giây đo bằng đồng hồ giả — không Task.Delay(61_000) (chậm một phút mà vẫn chập chờn).
/// Nguồn giả CHỈ sống ở đây (Đ3), đăng ký scoped vào ServiceCollection nhỏ để có IServiceScopeFactory thật,
/// với ValidateScopes bật — cache inject thẳng nguồn scoped (captive dependency) thì test ném ngay lúc resolve.
/// </summary>
public sealed class PermissionCacheTests
{
    private readonly ManualTime _time = new();
    private readonly FakeSource _source = new();
    private readonly IPermissionCache _cache;

    public PermissionCacheTests()
    {
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time)
            .AddScoped<IRolePermissionSource>(_ => _source)
            .AddSingleton<IPermissionCache, PermissionCache>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        _cache = services.GetRequiredService<IPermissionCache>();
    }

    [Fact]
    public async Task Giay_59_tra_du_lieu_cu_giay_61_doc_lai_nguon_va_nguon_goi_dung_2_lan()
    {
        _source.Current = new HashSet<string> { "post.hide" };
        Assert.Equal(["post.hide"], await _cache.GetAsync("MODERATOR"));

        // Admin gỡ post.hide trong DB — cache chưa biết.
        _source.Current = new HashSet<string>();

        _time.Now += TimeSpan.FromSeconds(59);
        Assert.Equal(["post.hide"], await _cache.GetAsync("MODERATOR"));

        _time.Now += TimeSpan.FromSeconds(2);   // giây 61
        Assert.Empty(await _cache.GetAsync("MODERATOR"));

        Assert.Equal(2, _source.Calls);
    }

    [Fact]
    public async Task Loi_tu_nguon_khong_bi_cache()
    {
        _source.Throw = new InvalidOperationException("DB chết");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.GetAsync("MODERATOR").AsTask());

        // DB sống lại ngay — KHÔNG phải chờ hết 60 giây mới hết 403.
        _source.Throw = null;
        _source.Current = new HashSet<string> { "post.hide" };
        Assert.Equal(["post.hide"], await _cache.GetAsync("MODERATOR"));

        Assert.Equal(2, _source.Calls);
    }

    [Fact]
    public async Task Moi_vai_tro_mot_entry_rieng()
    {
        _source.Current = new HashSet<string> { "post.create" };
        await _cache.GetAsync("USER");
        await _cache.GetAsync("MODERATOR");
        await _cache.GetAsync("USER");

        Assert.Equal(["USER", "MODERATOR"], _source.Roles);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSource : IRolePermissionSource
    {
        public IReadOnlySet<string> Current { get; set; } = new HashSet<string>();
        public Exception? Throw { get; set; }
        public List<string> Roles { get; } = [];
        public int Calls => Roles.Count;

        public Task<IReadOnlySet<string>> GetPermissionsAsync(string roleCode, CancellationToken ct = default)
        {
            Roles.Add(roleCode);
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Current);
        }
    }
}
