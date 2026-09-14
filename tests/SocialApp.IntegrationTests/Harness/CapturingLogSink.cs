using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Bắt log của app trong test. Program.cs dùng Serilog với <c>ReadFrom.Services</c> nên sink đăng ký bằng
/// <c>services.AddSingleton&lt;ILogEventSink&gt;(sink)</c> ở ConfigureTestServices nhận được mọi log — <c>ILoggerProvider</c> thì
/// KHÔNG (UseSerilog thay logger factory).
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
