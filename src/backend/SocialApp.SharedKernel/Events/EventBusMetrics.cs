using System.Diagnostics.Metrics;

namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Hai counter của event bus trên <c>Meter("SocialApp.Events")</c>, tag <c>event</c> = tên kiểu event. Dùng
/// <c>System.Diagnostics.Metrics</c> có sẵn trong .NET 8 thay vì bộ đếm tự chế: GĐ7 (<c>prometheus-net</c>, Đ-7.8) đọc Meter
/// này; GĐ7 C2 kiểm tên thật xuất ra ở <c>/metrics</c> và sửa ở đúng chỗ này nếu adapter thêm hậu tố.
///
/// Meter lấy từ <see cref="IMeterFactory"/> của container, không <c>static</c>: mỗi container (mỗi ca test) một Meter riêng,
/// <c>MeterListener</c> của ca này không đếm lẫn event của ca chạy song song.
/// </summary>
public sealed class EventBusMetrics
{
    public const string MeterName = "SocialApp.Events";
    public const string PublishedName = "socialapp_events_published_total";
    public const string DroppedName = "socialapp_events_dropped_total";

    private readonly Counter<long> _published;
    private readonly Counter<long> _dropped;

    public EventBusMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);
        _published = meter.CreateCounter<long>(PublishedName, description: "Event đã nhận vào hàng đợi, kể cả khi chưa có handler");
        _dropped = meter.CreateCounter<long>(DroppedName, description: "Event bị rơi vì hàng đợi đầy");
    }

    public void Published(Type eventType) => _published.Add(1, new KeyValuePair<string, object?>("event", eventType.Name));

    public void Dropped(Type eventType) => _dropped.Add(1, new KeyValuePair<string, object?>("event", eventType.Name));
}
