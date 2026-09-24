using System.Net;
using System.Net.Http.Json;
using SocialApp.Modules.Messaging.Application.Conversations;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// B1 GĐ5 — lời gọi REST của Messaging cho test (khuôn <see cref="ModulesTestClient"/>). Mỗi hàm một endpoint; bản <c>…OkAsync</c>
/// khẳng định mã thành công rồi đọc body. <c>actorId</c> là <c>sub</c> của JWT test — không cần dữ liệu nền.
/// </summary>
public sealed class MessagingTestClient(ModulesApiFactory factory)
{
    public ModulesTestClient Modules { get; } = new(factory);

    private HttpClient Http => Modules.Http;

    /// <summary>A và B có hồ sơ và là bạn — qua API thật (GĐ4 D2/D3), không INSERT thẳng (Mục 6.3, bàn giao GĐ4).</summary>
    public async Task FriendsAsync(Guid a, Guid b)
    {
        await Modules.PutProfileOkAsync(a, new { displayName = $"Người {a.ToString()[..4]}" });
        await Modules.PutProfileOkAsync(b, new { displayName = $"Người {b.ToString()[..4]}" });
        await Modules.MakeFriendsAsync(a, b);
    }

    public Task<HttpResponseMessage> OpenAsync(Guid actorId, object body, string role = "USER") =>
        SendAsync(HttpMethod.Post, "/api/v1/conversations", actorId, body, role);

    /// <summary>Mở hội thoại A–B, chấp nhận 201 hoặc 200.</summary>
    public async Task<ConversationResponse> OpenOkAsync(Guid actorId, Guid peerId)
    {
        using var response = await OpenAsync(actorId, new { userId = peerId });
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"POST /conversations: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ConversationResponse>(ModulesTestClient.Json))!;
    }

    public Task<HttpResponseMessage> GetAsync(Guid actorId, object conversationId) =>
        SendAsync(HttpMethod.Get, $"/api/v1/conversations/{conversationId}", actorId);

    public async Task<ConversationResponse> GetOkAsync(Guid actorId, Guid conversationId) =>
        await ReadOkAsync<ConversationResponse>(await GetAsync(actorId, conversationId));

    public Task<HttpResponseMessage> ListAsync(Guid actorId, string query = "") =>
        SendAsync(HttpMethod.Get, $"/api/v1/conversations{query}", actorId);

    public async Task<ConversationPage> ListOkAsync(Guid actorId, string query = "") =>
        await ReadOkAsync<ConversationPage>(await ListAsync(actorId, query));

    public async Task<long> UnreadOkAsync(Guid actorId) =>
        (await ReadOkAsync<UnreadCountResponse>(
            await SendAsync(HttpMethod.Get, "/api/v1/conversations/unread-count", actorId))).Total;

    public Task<HttpResponseMessage> HistoryAsync(Guid actorId, object conversationId, string query = "") =>
        SendAsync(HttpMethod.Get, $"/api/v1/conversations/{conversationId}/messages{query}", actorId);

    public async Task<MessagePage> HistoryOkAsync(Guid actorId, Guid conversationId, string query = "") =>
        await ReadOkAsync<MessagePage>(await HistoryAsync(actorId, conversationId, query));

    public Task<HttpResponseMessage> SendMessageAsync(Guid actorId, object conversationId, object body, string role = "USER") =>
        SendAsync(HttpMethod.Post, $"/api/v1/conversations/{conversationId}/messages", actorId, body, role);

    /// <summary>Gửi tin MỚI, khẳng định 201.</summary>
    public async Task<MessageResponse> SendOkAsync(Guid actorId, Guid conversationId, string content, Guid? clientMsgId = null)
    {
        using var response = await SendMessageAsync(
            actorId, conversationId, new { content, clientMsgId = clientMsgId ?? Guid.NewGuid() });
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"POST …/messages: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<MessageResponse>(ModulesTestClient.Json))!;
    }

    public Task<HttpResponseMessage> ReceiptAsync(Guid actorId, object conversationId, object body) =>
        SendAsync(HttpMethod.Post, $"/api/v1/conversations/{conversationId}/receipts", actorId, body);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, Guid actorId, object? body = null, string role = "USER")
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = ModulesTestClient.Bearer(actorId, role);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await Http.SendAsync(request);
    }

    private static async Task<T> ReadOkAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} "
              + await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>(ModulesTestClient.Json))!;
        }
    }
}
