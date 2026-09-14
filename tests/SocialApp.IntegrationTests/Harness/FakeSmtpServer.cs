using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// SMTP giả tối thiểu trên loopback, cổng ngẫu nhiên — nhận thư của <c>SmtpEmailSender</c> THẬT để test đọc được địa chỉ
/// và link mà không cần Mailpit. Chỉ đủ lệnh SmtpClient dùng khi không xác thực, không TLS.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly Task _acceptLoop;

    private FakeSmtpServer()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public static FakeSmtpServer Start() => new();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Phần DATA thô của từng thư (header + body chưa giải mã), theo thứ tự nhận.</summary>
    public IReadOnlyList<string> Messages => [.. _messages];

    /// <summary>Body đã giải mã của một thư thô (SmtpEmailSender gửi base64 UTF-8).</summary>
    public static string DecodeBody(string rawMessage)
    {
        var split = rawMessage.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headers = rawMessage[..split];
        var body = rawMessage[(split + 4)..];

        return headers.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c)))))
            : body;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = HandleAsync(client);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Dừng server.
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake-smtp");
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync("250 fake-smtp");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine).Append("\r\n");
                        _messages.Enqueue(data.ToString());
                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:   // MAIL FROM, RCPT TO, RSET, NOOP
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _acceptLoop;
        _stop.Dispose();
    }
}
