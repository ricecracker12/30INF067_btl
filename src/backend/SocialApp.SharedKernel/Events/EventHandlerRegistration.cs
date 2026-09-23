namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Một dòng "event X do handler H xử lý", đăng ký singleton bởi
/// <see cref="EventBusServiceCollectionExtensions.AddIntegrationEventHandler{TEvent, THandler}"/>. Bus biết kiểu handler
/// <b>trước khi</b> mở scope — nên mở được một scope riêng cho từng handler, và biết "không có handler" mà không mở scope nào.
/// <paramref name="Invoke"/> gọi handler không qua reflection.
/// </summary>
public sealed record EventHandlerRegistration(
    Type EventType,
    Type HandlerType,
    Func<object, IIntegrationEvent, CancellationToken, Task> Invoke);
