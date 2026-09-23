using System.Net;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// GĐ7 C1: /metrics cho Prometheus. Không chạm Postgres/Redis — cùng loại với SmokeEndpointsTests.
/// Test thứ hai là test đáng giá: cảnh báo "5xx > 1%" (giai-doan-7.md Mục 5.3) chỉ kêu được nếu lỗi 500 được ĐẾM là
/// 500. Đặt <c>UseHttpMetrics()</c> sau <c>UseExceptionHandler</c> thì exception đi xuyên qua lúc status còn 200.
/// </summary>
public sealed class MetricsEndpointTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    /// <summary>Prometheus scrape không có token — nhận 401 nghĩa là fallback policy đang chặn, target DOWN.</summary>
    [Fact]
    public async Task Metrics_khong_can_token()
    {
        var response = await Client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http_request_duration_seconds", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// C2: bảy chuỗi nghiệp vụ phải có mặt NGAY sau khởi động, không đợi sự kiện đầu tiên (BusinessMetrics.Initialize).
    /// Chỉ kiểm có mặt, không kiểm giá trị 0: test khác cùng process có thể đã tăng chúng. <c>result="failed"</c> không
    /// test nào kích được, nên thiếu Initialize là dòng đó vắng dù chạy chung hay chạy riêng.
    /// </summary>
    [Fact]
    public async Task Chi_so_nghiep_vu_co_mat_tu_luc_khoi_dong()
    {
        var dong = (await Client.GetStringAsync("/metrics")).Split('\n');

        string[] chuoi =
        [
            "socialapp_login_failed_total ",
            "socialapp_posts_created_total ",
            "socialapp_presign_issued_total{purpose=\"post\"} ",
            "socialapp_presign_issued_total{purpose=\"avatar\"} ",
            "socialapp_media_cleanup_runs_total{result=\"ran\"} ",
            "socialapp_media_cleanup_runs_total{result=\"lock\"} ",
            "socialapp_media_cleanup_runs_total{result=\"failed\"} ",
        ];
        Assert.All(chuoi, c => Assert.Contains(dong, l => l.StartsWith(c, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Loi_500_duoc_dem_dung_ma_500()
    {
        var boom = await Client.GetAsync("/api/v1/ping/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, boom.StatusCode);

        var metrics = await Client.GetStringAsync("/metrics");
        var dongDemBoom = metrics.Split('\n')
            .Where(l => l.StartsWith("http_request_duration_seconds_count{") && l.Contains("action=\"Boom\""))
            .ToList();

        Assert.NotEmpty(dongDemBoom);
        Assert.All(dongDemBoom, l => Assert.Contains("code=\"500\"", l));
    }
}
