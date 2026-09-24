using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Events;
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
    /// C2 (GĐ7) + GĐ5: chín chuỗi đếm nghiệp vụ và histogram đẩy tin phải có mặt NGAY sau khởi động, không đợi sự kiện đầu tiên (BusinessMetrics.Initialize).
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
            // GĐ5 (D5, D7): hai kênh gửi tin + histogram đẩy tin.
            "socialapp_messages_sent_total{channel=\"hub\"} ",
            "socialapp_messages_sent_total{channel=\"rest\"} ",
            "socialapp_message_push_seconds_count ",
            "socialapp_revocation_failures_total ",   // GĐ6 D3 (Đ-6.6)
        ];
        Assert.All(chuoi, c => Assert.Contains(dong, l => l.StartsWith(c, StringComparison.Ordinal)));
    }

    /// <summary>
    /// GĐ6: hai counter của event bus phải LÊN ĐƯỢC <c>/metrics</c> — cảnh báo "event bị rơi" (R6-10) dựa vào nó. Bản đầu (C0) đếm
    /// bằng <c>System.Diagnostics.Metrics</c>, thứ GĐ7 C2 đã thử bốn cách mà không xuất ra được; ca này đỏ với bản đó.
    /// Có mặt ngay từ lúc khởi động cho mọi kiểu event (cùng lý do <see cref="Chi_so_nghiep_vu_co_mat_tu_luc_khoi_dong"/>).
    /// </summary>
    [Fact]
    public async Task Chi_so_event_bus_co_mat_tu_luc_khoi_dong_cho_moi_kieu_event()
    {
        var dong = (await Client.GetStringAsync("/metrics")).Split('\n');

        var kieuEvent = typeof(IIntegrationEvent).Assembly.GetTypes()
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Select(t => t.Name)
            .ToList();
        Assert.Equal(6, kieuEvent.Count);   // canh gác: phản chiếu không tìm ra gì thì ca bên dưới xanh trong chân không
        Assert.All(kieuEvent, e =>
        {
            Assert.Contains(dong, l => l.StartsWith($"socialapp_events_published_total{{event=\"{e}\"}} ", StringComparison.Ordinal));
            Assert.Contains(dong, l => l.StartsWith($"socialapp_events_dropped_total{{event=\"{e}\"}} ", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Event_da_phat_duoc_dem_tren_metrics()
    {
        _ = Client;   // dựng host trước khi lấy service
        var bus = factory.Services.GetRequiredService<InProcessEventBus>();

        bus.Publish(new MetricsProbeEvent());
        await bus.DrainAsync(TimeSpan.FromSeconds(5));

        var dong = (await Client.GetStringAsync("/metrics")).Split('\n')
            .SingleOrDefault(l => l.StartsWith("socialapp_events_published_total{event=\"MetricsProbeEvent\"} ", StringComparison.Ordinal));
        Assert.NotNull(dong);
        Assert.True(double.Parse(dong.Split(' ')[1], CultureInfo.InvariantCulture) >= 1, dong);
    }

    /// <summary>Kiểu event riêng của ca này — không module nào có handler cho nó, publish là no-op có đếm.</summary>
    private sealed record MetricsProbeEvent : IIntegrationEvent;

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
