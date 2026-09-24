namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Người tiêu thụ một loại event. Mỗi lần chạy nằm trong <b>scope DI riêng</b> — hai handler của cùng một event không chung
/// <c>DbContext</c> (Đ-6.2). Lỗi của handler bị bắt và ghi log, không lan sang handler khác hay về request gốc.
///
/// <b>Đăng ký bằng <see cref="EventBusServiceCollectionExtensions.AddIntegrationEventHandler{TEvent, THandler}"/></b>, không
/// bằng <c>AddScoped&lt;IIntegrationEventHandler&lt;X&gt;, H&gt;()</c>: bus đọc danh sách đăng ký, không đọc interface này — đăng
/// ký thẳng thì compile được nhưng handler không bao giờ chạy.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
