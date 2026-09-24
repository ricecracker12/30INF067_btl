using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.Modules.Messaging.Application.Realtime;
using SocialApp.Modules.Messaging.Presentation;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// B1 GĐ5 — client hub đi ĐÚNG đường client thật đi (Đ-5.16): chỉ WebSockets, <c>SkipNegotiation</c>, vé đặt vào
/// <c>?access_token=</c> của URL WebSocket (như SignalR JS trong trình duyệt), xin MỘT vé cho MỖI lần kết nối.
///
/// Không dùng <c>AccessTokenProvider</c> của client .NET: ngoài trình duyệt nó gắn token vào header <c>Authorization</c>, không
/// vào query — tức là đi một đường mà trình duyệt không bao giờ đi, và scheme vé (chỉ đọc query) sẽ từ chối.
/// </summary>
public static class RealtimeTestClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary><c>POST /api/v1/realtime/tickets</c> bằng bearer của <paramref name="userId"/>; khẳng định 201, trả vé.</summary>
    public static async Task<string> IssueTicketAsync(
        HttpClient http, Guid userId, string role = "USER", DateTimeOffset? issuedAt = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/realtime/tickets");
        request.Headers.Authorization = new("Bearer", TestJwt.Create(role, userId, issuedAt));
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RealtimeTicketResponse>(Json);
        Assert.Equal(30, body!.ExpiresIn);
        return body.Ticket;
    }

    /// <summary>
    /// Kết nối hub với vé do <paramref name="ticket"/> cung cấp — gọi MỘT lần cho mỗi lần bắt tay (kể cả tự nối lại).
    /// <paramref name="ticket"/> trả <c>null</c> = bắt tay không vé (HUB-01).
    /// </summary>
    public static HubConnection Build<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, Func<Task<string?>> ticket, string path = ChatHub.Path)
        where TEntryPoint : class
    {
        var server = factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, path), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = true;
                o.WebSocketFactory = async (context, ct) =>
                {
                    var value = await ticket();
                    var uri = value is null
                        ? context.Uri
                        : new Uri(QueryHelpers.AddQueryString(context.Uri.ToString(), "access_token", value));
                    return await server.CreateWebSocketClient().ConnectAsync(uri, ct);
                };
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
            .Build();
    }

    /// <summary>Kết nối đã bắt tay xong cho <paramref name="userId"/>: mỗi lần bắt tay xin một vé mới qua REST.</summary>
    public static async Task<HubConnection> ConnectAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, HttpClient http, Guid userId, string role = "USER")
        where TEntryPoint : class
    {
        var connection = Build(factory, async () => await IssueTicketAsync(http, userId, role));
        await connection.StartAsync();
        return connection;
    }

    /// <summary>
    /// Bắt tay phải bị từ chối 401. TestServer báo bắt tay hỏng bằng exception có mã trạng thái trong thông điệp.
    /// </summary>
    public static async Task AssertHandshakeRejectedAsync(HubConnection connection)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Contains("401", ex.ToString());
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    /// <summary>Chờ tới khi kết nối đóng (server cắt), tối đa <paramref name="timeout"/>.</summary>
    public static async Task WaitClosedAsync(HubConnection connection, TimeSpan timeout)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        if (connection.State == HubConnectionState.Disconnected)
            return;

        var finished = await Task.WhenAny(closed.Task, Task.Delay(timeout));
        Assert.True(finished == closed.Task, $"Kết nối không bị server cắt trong {timeout.TotalSeconds} giây.");
    }

    /// <summary>Dịch vụ của host (Redis store, revocation) cho test đặt trạng thái.</summary>
    public static T Service<T>(this WebApplicationFactory<Program> factory) where T : notnull =>
        factory.Services.GetRequiredService<T>();
}
