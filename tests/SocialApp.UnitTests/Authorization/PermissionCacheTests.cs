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

    /// <summary>GĐ6 C3 (Đ-6.10): sửa quyền xong gọi Invalidate thì lần đọc kế tiếp về nguồn NGAY — không đợi 60 giây.</summary>
    [Fact]
    public async Task Invalidate_doc_lai_nguon_ngay_khong_doi_TTL()
    {
        _source.Current = new HashSet<string> { "post.create" };
        await _cache.GetAsync("USER");

        _source.Current = new HashSet<string>();   // Admin gỡ post.create
        _cache.Invalidate("USER");

        Assert.Empty(await _cache.GetAsync("USER"));   // cùng giây — đồng hồ không nhúc nhích
        Assert.Equal(2, _source.Calls);
    }

    [Fact]
    public async Task Invalidate_mot_vai_tro_khong_dung_vai_tro_khac_InvalidateAll_xoa_het()
    {
        await _cache.GetAsync("USER");
        await _cache.GetAsync("MODERATOR");

        _cache.Invalidate("USER");
        await _cache.GetAsync("USER");
        await _cache.GetAsync("MODERATOR");
        Assert.Equal(["USER", "MODERATOR", "USER"], _source.Roles);

        _cache.InvalidateAll();
        await _cache.GetAsync("USER");
        await _cache.GetAsync("MODERATOR");
        Assert.Equal(["USER", "MODERATOR", "USER", "USER", "MODERATOR"], _source.Roles);
    }

    /// <summary>
    /// PERM-03 (L-C5): lần nạp BẮT ĐẦU trước Invalidate (đọc DB trước COMMIT của thay đổi) không được để kết quả cũ nằm lại trong
    /// cache. Không có thế hệ thì lần đọc sau trả quyền cũ thêm 60 giây dù đã invalidate — không test một-luồng nào khác thấy.
    /// Chờ bằng trạng thái (nguồn báo "đã vào"), không bằng thời gian.
    /// </summary>
    [Fact]
    public async Task PERM_03_nap_dang_do_khi_Invalidate_thi_ket_qua_cu_khong_nam_lai_trong_cache()
    {
        _source.Current = new HashSet<string> { "post.create" };   // quyền CŨ, đọc trước COMMIT
        var gate = new TaskCompletionSource();
        _source.Gate = gate;

        var loading = _cache.GetAsync("USER").AsTask();
        await _source.Entered.Task;                                  // lần nạp đã đọc nguồn, đang chờ

        _source.Current = new HashSet<string>();                     // Admin COMMIT gỡ post.create …
        _cache.Invalidate("USER");                                   // … rồi invalidate
        _source.Gate = null;
        gate.SetResult();

        Assert.Equal(["post.create"], await loading);                // request đang dở vẫn nhận kết quả của nó
        Assert.Empty(await _cache.GetAsync("USER"));                 // nhưng lần sau KHÔNG dùng kết quả cũ
        Assert.Equal(2, _source.Calls);
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

        /// <summary>Có giá trị → lần gọi đọc <see cref="Current"/> NGAY, báo <see cref="Entered"/>, rồi chờ cổng mới trả về.</summary>
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlySet<string>> GetPermissionsAsync(string roleCode, CancellationToken ct = default)
        {
            Roles.Add(roleCode);
            if (Throw is not null)
                throw Throw;

            var snapshot = Current;
            if (Gate is { } gate)
            {
                Entered.TrySetResult();
                await gate.Task;
            }
            return snapshot;
        }
    }
}
