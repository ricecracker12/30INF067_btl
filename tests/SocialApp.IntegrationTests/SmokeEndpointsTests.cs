using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// GĐ0 Walking Skeleton: xác nhận pipeline đi hết (routing → middleware → controller → JSON) và
/// error model RFC 7807. Không chạm Postgres/Redis nên an toàn trên CI (chỉ /health/live + /ping).
/// </summary>
public sealed class SmokeEndpointsTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Health_live_returns_200()
    {
        var response = await Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// /health/ready phải công khai. ApiFactory trỏ Postgres/Redis vào cổng không có gì nên kỳ vọng 503 —
    /// nhận 401 nghĩa là fallback policy đang chặn healthcheck, và container staging sẽ bị báo unhealthy.
    /// Có thể mất vài giây vì client Redis chờ timeout kết nối.
    /// </summary>
    [Fact]
    public async Task Health_ready_khong_can_token()
    {
        var response = await Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Ping_returns_pong_with_traceId()
    {
        var response = await Client.GetAsync("/api/v1/ping");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PingDto>();
        Assert.Equal("pong", body!.Message);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Unhandled_error_returns_rfc7807_problem_details()
    {
        var response = await Client.GetAsync("/api/v1/ping/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Equal(500, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    private sealed record PingDto(string Message, string TraceId);
    private sealed record ProblemDto(string Title, int Status, string? TraceId);
}
