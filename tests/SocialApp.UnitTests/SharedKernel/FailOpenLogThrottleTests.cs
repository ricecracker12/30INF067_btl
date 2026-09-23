using SocialApp.SharedKernel.Redis;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// Giới hạn tần suất log fail-open (báo cáo k6 sơ bộ GĐ4, lượt 3): dòng đầu ghi ngay, trong cửa sổ chỉ đếm, dòng kế tiếp sau
/// cửa sổ mang số lần đã nuốt, và các loại cảnh báo không chặn nhau. Đồng hồ giả — không ngủ 30 giây.
/// </summary>
public sealed class FailOpenLogThrottleTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-23T04:00:00Z");

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualClock _clock = new();

    [Fact]
    public void Dong_dau_tien_ghi_ngay_voi_0_lan_bo_qua()
    {
        var throttle = new FailOpenLogThrottle(_clock);

        Assert.True(throttle.ShouldLog("feed-page", out var suppressed));
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void Trong_cua_so_chi_dem_khong_ghi()
    {
        var throttle = new FailOpenLogThrottle(_clock);
        throttle.ShouldLog("feed-page", out _);

        for (var i = 0; i < 1000; i++)
        {
            _clock.Now = _clock.Now.AddMilliseconds(10);
            Assert.False(throttle.ShouldLog("feed-page", out _));
        }
    }

    /// <summary>Dòng kế tiếp sau cửa sổ mang đúng số lần đã nuốt, rồi bộ đếm về 0.</summary>
    [Fact]
    public void Het_cua_so_thi_ghi_kem_so_lan_da_nuot_roi_dem_lai_tu_0()
    {
        var throttle = new FailOpenLogThrottle(_clock);
        throttle.ShouldLog("feed-page", out _);
        for (var i = 0; i < 42; i++)
            throttle.ShouldLog("feed-page", out _);

        _clock.Now = _clock.Now.AddSeconds(30);
        Assert.True(throttle.ShouldLog("feed-page", out var suppressed));
        Assert.Equal(42, suppressed);

        _clock.Now = _clock.Now.AddSeconds(30);
        Assert.True(throttle.ShouldLog("feed-page", out var afterReset));
        Assert.Equal(0, afterReset);
    }

    /// <summary>Mỗi loại một khe: cảnh báo thu hồi token vừa ghi không được nuốt dòng đầu của cache feed.</summary>
    [Fact]
    public void Cac_loai_canh_bao_khong_chan_nhau()
    {
        var throttle = new FailOpenLogThrottle(_clock);

        Assert.True(throttle.ShouldLog("token-revocation", out _));
        Assert.True(throttle.ShouldLog("feed-sources-read", out _));
        Assert.True(throttle.ShouldLog("feed-page", out _));
        Assert.False(throttle.ShouldLog("token-revocation", out _));
    }

    /// <summary>500 request/s đồng thời (đúng tải của lượt 3): đúng MỘT lượt được ghi trong cửa sổ, không mất lần đếm nào.</summary>
    [Fact]
    public void Nhieu_luong_cung_luc_chi_mot_dong_va_dem_du()
    {
        var throttle = new FailOpenLogThrottle(_clock);
        var logged = 0;

        Parallel.For(0, 500, _ =>
        {
            if (throttle.ShouldLog("feed-page", out _))
                Interlocked.Increment(ref logged);
        });

        Assert.Equal(1, logged);
        _clock.Now = _clock.Now.AddSeconds(31);
        Assert.True(throttle.ShouldLog("feed-page", out var suppressed));
        Assert.Equal(499, suppressed);
    }
}
