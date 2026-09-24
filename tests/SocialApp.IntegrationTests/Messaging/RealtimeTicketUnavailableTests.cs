using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Messaging.Application;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// Đ-5.9 "Redis chết → không cấp được vé → 503, không fail-open". <see cref="ModulesApiFactory"/> mặc định trỏ Redis KHÔNG tới
/// được — đúng cảnh cần kiểm. FE phân nhánh theo <c>type</c> (không theo status) để chuyển fallback REST (Đ-5.12).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RealtimeTicketUnavailableTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Redis_khong_toi_duoc_xin_ve_503_type_realtime_unavailable()
    {
        var http = new ModulesTestClient(factory).Http;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/realtime/tickets");
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());

        using var response = await http.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(MessagingErrors.RealtimeUnavailableType, problem!.Type);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }
}
