using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using SocialApp.SharedKernel.Events;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// Bốn luật của event bus (Đ-6.2) chạy trên CI: phát không chờ handler (EVT-01), handler lỗi không lan và log không mang payload
/// (EVT-02), hàng đợi đầy thì rơi + đếm + cảnh báo có ngưỡng (EVT-03), mỗi handler một scope DI (EVT-04), một instance cho cả
/// người phát lẫn BackgroundService (EVT-05).
///
/// Dùng record RIÊNG của test, không dùng sáu record thật — test của bus không đỏ khi A, B xin đổi một chữ ký. Không ca nào chờ
/// bằng <c>Task.Delay</c>: chờ <c>DrainAsync</c> hoặc chờ một <see cref="TaskCompletionSource"/> báo trạng thái.
///
/// Metric đọc từ bản export của registry prometheus-net — đúng chữ <c>/metrics</c> in ra. Counter là của cả process nên khẳng định
/// trên phần TĂNG kể từ đầu ca; nhãn <c>ProbeEvent</c> chỉ lớp này phát, và các ca trong một lớp xUnit chạy tuần tự.
/// </summary>
public sealed class InProcessEventBusTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private const string Published = "socialapp_events_published_total";
    private const string Dropped = "socialapp_events_dropped_total";

    private sealed record ProbeEvent(Guid Id) : IIntegrationEvent;

    [Fact]
    public async Task EVT_01_Publish_khi_chua_co_handler_la_no_op_co_dem()
    {
        await using var bus = await BusHarness.StartAsync(_ => { });

        bus.Publisher.Publish(new ProbeEvent(Guid.NewGuid()));
        await bus.Bus.DrainAsync(Wait);

        Assert.Equal(1, await bus.Metrics.DeltaAsync(Published));
        Assert.Equal(0, await bus.Metrics.DeltaAsync(Dropped));
        Assert.Empty(bus.Logs.AtLeast(LogLevel.Warning));
    }

    [Fact]
    public async Task EVT_02_Handler_nem_loi_khong_lan_ra_va_log_khong_mang_payload()
    {
        await using var bus = await BusHarness.StartAsync(s =>
            s.AddIntegrationEventHandler<ProbeEvent, ThrowingHandler>());
        var id = Guid.NewGuid();

        bus.Publisher.Publish(new ProbeEvent(id));   // không ném
        await bus.Bus.DrainAsync(Wait);

        var error = Assert.Single(bus.Logs.AtLeast(LogLevel.Error));
        Assert.Contains(nameof(ThrowingHandler), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ProbeEvent), error.Message, StringComparison.Ordinal);
        // Message đã render CHƯA đủ — logger còn giữ property; kiểm cả hai.
        Assert.DoesNotContain(id.ToString(), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(error.Properties, p =>
            Assert.DoesNotContain(id.ToString(), p.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase));

        // Bus vẫn sống: event kế tiếp vẫn tới handler.
        bus.Publisher.Publish(new ProbeEvent(Guid.NewGuid()));
        await bus.Bus.DrainAsync(Wait);
        Assert.Equal(2, bus.Probe.Calls.Count);
    }

    [Fact]
    public async Task EVT_03_Hang_doi_day_thi_roi_dem_canh_bao_mot_lan_khong_chan_nguoi_phat()
    {
        await using var bus = await BusHarness.StartAsync(s =>
        {
            s.Configure<EventBusOptions>(o => o.Capacity = 2);
            s.AddIntegrationEventHandler<ProbeEvent, BlockingHandler>();
        });
        var events = Enumerable.Range(0, 5).Select(_ => new ProbeEvent(Guid.NewGuid())).ToArray();

        bus.Publisher.Publish(events[0]);
        // Chờ handler ĐÃ lấy e1 khỏi hàng đợi — phát liền một mạch thì số rơi là 2 hoặc 3 tùy lượt.
        await bus.Probe.Entered.Task.WaitAsync(Wait);
        // e1 đã rời hàng đợi nhưng handler còn chạy → DrainAsync CHƯA được xong. Giảm bộ đếm dở dang ngay lúc đọc khỏi hàng
        // (trước khi handler xong) thì DrainAsync trả về tức thì và mọi test sau khẳng định trước khi handler chạy.
        var drainWhileRunning = bus.Bus.DrainAsync(Wait);
        Assert.False(drainWhileRunning.IsCompleted);
        foreach (var e in events.Skip(1))
            bus.Publisher.Publish(e);   // e2, e3 vào hàng (dung lượng 2); e4, e5 rơi — không chặn

        Assert.Equal(2, await bus.Metrics.DeltaAsync(Dropped));
        Assert.Single(bus.Logs.AtLeast(LogLevel.Warning));   // ngưỡng: hai lần rơi, MỘT dòng

        bus.Probe.Gate.SetResult();
        await drainWhileRunning;
        Assert.Equal(events.Take(3).Select(e => e.Id), bus.Probe.Calls.Select(c => ((ProbeEvent)c.Event).Id));
    }

    [Fact]
    public async Task EVT_04_Moi_handler_mot_scope_rieng_handler_loi_khong_chan_handler_sau()
    {
        await using var bus = await BusHarness.StartAsync(s =>
        {
            s.AddScoped<ScopedMarker>();
            s.AddIntegrationEventHandler<ProbeEvent, ScopedThrowingHandler>();
            s.AddIntegrationEventHandler<ProbeEvent, ScopedRecordingHandler>();
        });

        bus.Publisher.Publish(new ProbeEvent(Guid.NewGuid()));
        await bus.Bus.DrainAsync(Wait);

        Assert.Equal(2, bus.Probe.Calls.Count);   // handler 1 ném, handler 2 vẫn chạy
        Assert.NotSame(bus.Probe.Calls[0].Marker, bus.Probe.Calls[1].Marker);
    }

    [Fact]
    public async Task EVT_05_Mot_instance_cho_nguoi_phat_va_BackgroundService()
    {
        await using var bus = await BusHarness.StartAsync(_ => { });

        Assert.Same(bus.Bus, bus.Publisher);
        Assert.Same(bus.Bus, Assert.Single(bus.Services.GetServices<IHostedService>()));
    }

    [Fact]
    public void Dang_ky_trung_mot_handler_cho_mot_event_thi_nem_luc_dung()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddInProcessEventBus()
            .AddIntegrationEventHandler<ProbeEvent, ThrowingHandler>()
            .AddIntegrationEventHandler<ProbeEvent, ThrowingHandler>()
            .BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<InProcessEventBus>());
        Assert.Contains(nameof(ThrowingHandler), ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- handler giả

    private sealed class Probe
    {
        public List<(IIntegrationEvent Event, ScopedMarker? Marker)> Calls { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ScopedMarker;

    private sealed class ThrowingHandler(Probe probe) : IIntegrationEventHandler<ProbeEvent>
    {
        public Task HandleAsync(ProbeEvent integrationEvent, CancellationToken ct)
        {
            probe.Calls.Add((integrationEvent, null));
            throw new InvalidOperationException("handler giả hỏng");
        }
    }

    private sealed class BlockingHandler(Probe probe) : IIntegrationEventHandler<ProbeEvent>
    {
        public async Task HandleAsync(ProbeEvent integrationEvent, CancellationToken ct)
        {
            probe.Calls.Add((integrationEvent, null));
            probe.Entered.TrySetResult();
            await probe.Gate.Task.WaitAsync(ct);
        }
    }

    private sealed class ScopedThrowingHandler(Probe probe, ScopedMarker marker) : IIntegrationEventHandler<ProbeEvent>
    {
        public Task HandleAsync(ProbeEvent integrationEvent, CancellationToken ct)
        {
            probe.Calls.Add((integrationEvent, marker));
            throw new InvalidOperationException("handler giả hỏng");
        }
    }

    private sealed class ScopedRecordingHandler(Probe probe, ScopedMarker marker) : IIntegrationEventHandler<ProbeEvent>
    {
        public Task HandleAsync(ProbeEvent integrationEvent, CancellationToken ct)
        {
            probe.Calls.Add((integrationEvent, marker));
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------- harness

    /// <summary>Container trần + bus đã <c>StartAsync</c>; log và metric bắt riêng cho từng ca.</summary>
    private sealed class BusHarness : IAsyncDisposable
    {
        private BusHarness(ServiceProvider services, MetricsRecorder metrics)
        {
            Services = services;
            Bus = services.GetRequiredService<InProcessEventBus>();
            Publisher = services.GetRequiredService<IEventPublisher>();
            Probe = services.GetRequiredService<Probe>();
            Logs = services.GetRequiredService<RecordingLoggerProvider>();
            Metrics = metrics;
        }

        public ServiceProvider Services { get; }
        public InProcessEventBus Bus { get; }
        public IEventPublisher Publisher { get; }
        public Probe Probe { get; }
        public RecordingLoggerProvider Logs { get; }
        public MetricsRecorder Metrics { get; }

        public static async Task<BusHarness> StartAsync(Action<IServiceCollection> configure)
        {
            var logs = new RecordingLoggerProvider();
            var services = new ServiceCollection()
                .AddSingleton(logs)
                .AddLogging(b => b.AddProvider(logs))
                .AddSingleton<Probe>()
                .AddInProcessEventBus();
            configure(services);

            var harness = new BusHarness(services.BuildServiceProvider(), await MetricsRecorder.StartAsync());
            await harness.Bus.StartAsync(CancellationToken.None);
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            Probe.Gate.TrySetResult();   // ca EVT-03 đỏ giữa chừng thì handler không treo StopAsync
            await Bus.StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
        }
    }

    /// <summary>Giá trị hai counter cho nhãn <c>ProbeEvent</c> lúc bắt đầu ca; <see cref="DeltaAsync"/> trả phần tăng thêm.</summary>
    private sealed class MetricsRecorder
    {
        private readonly Dictionary<string, double> _start;

        private MetricsRecorder(Dictionary<string, double> start) => _start = start;

        public static async Task<MetricsRecorder> StartAsync() =>
            new(new Dictionary<string, double>
            {
                [Published] = await ReadAsync(Published),
                [Dropped] = await ReadAsync(Dropped),
            });

        public async Task<double> DeltaAsync(string counter) => await ReadAsync(counter) - _start[counter];

        private static async Task<double> ReadAsync(string counter)
        {
            using var stream = new MemoryStream();
            await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
            var prefix = $"{counter}{{event=\"{nameof(ProbeEvent)}\"}} ";
            var line = System.Text.Encoding.UTF8.GetString(stream.ToArray()).Split('\n')
                .FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            return line is null ? 0 : double.Parse(line[prefix.Length..], CultureInfo.InvariantCulture);
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> AtLeast(LogLevel level) => _entries.Where(e => e.Level >= level).ToList();

        public ILogger CreateLogger(string categoryName) => new Logger(_entries);

        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception),
                    state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
        }
    }
}
