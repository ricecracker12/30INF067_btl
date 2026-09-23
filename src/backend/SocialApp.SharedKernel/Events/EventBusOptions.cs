namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Cấu hình event bus. KHÔNG bind từ cấu hình — không có key <c>.env</c> nào; test đặt nhỏ bằng
/// <c>services.Configure&lt;EventBusOptions&gt;(o =&gt; o.Capacity = 2)</c>.
/// </summary>
public sealed class EventBusOptions
{
    /// <summary>Số event tối đa chờ trong hàng đợi (Đ-6.2 luật 4). Tràn thì rơi event mới, không chặn người phát.</summary>
    public int Capacity { get; set; } = 10_000;
}
