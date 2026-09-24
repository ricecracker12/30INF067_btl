using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.SharedKernel.Observability;
using SocialApp.SharedKernel.Redis;

namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Event bus trong tiến trình (Đ-6.2): <see cref="Channel"/> có giới hạn + một <see cref="BackgroundService"/> tiêu thụ tuần tự.
/// Một lớp giữ cả hai vai để chắc chắn chỉ có MỘT hàng đợi — xem <see cref="EventBusServiceCollectionExtensions.AddInProcessEventBus"/>.
///
/// <list type="bullet">
/// <item><see cref="Publish"/> ghi vào hàng đợi rồi trả về; không chờ handler, không ném khi đầy.</item>
/// <item>Mỗi handler chạy trong một scope DI riêng; lỗi của từng handler bị bắt, ghi log, handler kế tiếp vẫn chạy.</item>
/// <item>Log chỉ mang tên kiểu event, số thứ tự phong bì và tên handler — KHÔNG bao giờ record (<c>ToString()</c> của record in
/// mọi id người dùng).</item>
/// <item>Mất event khi process chết giữa <c>COMMIT</c> và lúc handler chạy — chấp nhận có ghi lại (Đ-6.2, Mục 13 #10).</item>
/// </list>
/// Một handler chậm chặn cả hàng đợi (một người tiêu thụ). Nếu k6 của GĐ8 thấy <c>dropped &gt; 0</c> vì lý do đó: thêm timeout từng
/// handler trước khi nghĩ tới tăng dung lượng.
/// </summary>
public sealed class InProcessEventBus : BackgroundService, IEventPublisher
{
    private const string DroppedLogKind = "event-bus-full";

    private readonly IServiceScopeFactory _scopes;
    private readonly ILookup<Type, EventHandlerRegistration> _handlers;
    private readonly FailOpenLogThrottle _throttle;
    private readonly ILogger<InProcessEventBus> _logger;
    private readonly int _capacity;
    private readonly Channel<Envelope> _channel;

    private long _sequence;

    // Đã nhận mà chưa xử lý xong và chưa rơi. Giảm SAU khi mọi handler của event đã chạy xong — DrainAsync đọc số này.
    private long _pending;

    public InProcessEventBus(
        IServiceScopeFactory scopes,
        IEnumerable<EventHandlerRegistration> registrations,
        IOptions<EventBusOptions> options,
        FailOpenLogThrottle throttle,
        ILogger<InProcessEventBus> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(options);

        var list = registrations.ToList();
        var duplicate = list.GroupBy(r => (r.EventType, r.HandlerType)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Handler {duplicate.Key.HandlerType.Name} đăng ký hai lần cho {duplicate.Key.EventType.Name} — mỗi event sẽ bị xử lý hai lần.");

        _scopes = scopes;
        _handlers = list.ToLookup(r => r.EventType);
        _throttle = throttle;
        _logger = logger;
        _capacity = options.Value.Capacity;

        // DropWrite: khi đầy, phần tử đang ghi bị bỏ và TryWrite VẪN trả true — chỉ callback itemDropped biết có rơi.
        _channel = Channel.CreateBounded<Envelope>(
            new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            OnDropped);
    }

    /// <inheritdoc />
    public void Publish(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Interlocked.Increment(ref _pending);
        BusinessMetrics.EventPublished(integrationEvent.GetType());
        _channel.Writer.TryWrite(new Envelope(integrationEvent, Interlocked.Increment(ref _sequence)));
    }

    /// <summary>
    /// CHỈ test gọi (qua harness <c>DrainEventsAsync</c>): chờ tới khi không còn event nào trong hàng đợi hay đang chạy dở.
    /// Hỏi trạng thái mỗi 10 ms — điều kiện dừng là trạng thái thật, không phải thời gian đoán. Quá hạn thì ném, nói rõ còn bao
    /// nhiêu event dở: test đỏ vì bus kẹt phải đọc ra được là bus kẹt.
    /// </summary>
    public async Task DrainAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Interlocked.Read(ref _pending) > 0)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Event bus chưa rỗng sau {timeout.TotalSeconds:0.#}s: còn {Interlocked.Read(ref _pending)} event dở dang.");
            await Task.Delay(10);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(envelope, stoppingToken);
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private async Task DispatchAsync(Envelope envelope, CancellationToken stoppingToken)
    {
        var eventType = envelope.Event.GetType();

        // Mỗi handler MỘT scope: hai handler chung scope là chung DbContext — handler 1 ném sau khi đã Add, handler 2
        // SaveChangesAsync là ghi luôn phần dở của handler 1.
        foreach (var registration in _handlers[eventType])
        {
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
                await registration.Invoke(handler, envelope.Event, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // KHÔNG đưa envelope.Event vào log: record in mọi id người dùng.
                _logger.LogError(ex, "Handler {Handler} lỗi khi xử lý {EventType} #{Sequence}",
                    registration.HandlerType.Name, eventType.Name, envelope.Sequence);
            }
        }
    }

    private void OnDropped(Envelope envelope)
    {
        Interlocked.Decrement(ref _pending);
        var eventType = envelope.Event.GetType();
        BusinessMetrics.EventDropped(eventType);

        if (_throttle.ShouldLog(DroppedLogKind, out var suppressed))
            _logger.LogWarning(
                "Hàng đợi event đầy ({Capacity}), rơi {EventType} #{Sequence}; {Suppressed} lần rơi trước đó không ghi log",
                _capacity, eventType.Name, envelope.Sequence, suppressed);
    }

    private sealed record Envelope(IIntegrationEvent Event, long Sequence);
}
