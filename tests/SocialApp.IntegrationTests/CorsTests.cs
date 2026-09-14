using System.Net;

namespace SocialApp.IntegrationTests;

/// <summary>
/// D4 — CORS cho lane frontend (quyết định 7 của cổng mở): origin tường minh + <c>AllowCredentials</c>. Chạy trên
/// <see cref="ApiFactory"/> (Development → origin <c>http://localhost:3000</c> từ appsettings.Development.json), không cần DB.
/// Tên header và giá trị kỳ vọng viết tay.
///
/// Thứ tự <c>UseCors</c> trước <c>UseAuthentication</c>/<c>UseAuthorization</c> được canh bằng HAI test trên <c>/me</c> (endpoint
/// đòi token): preflight không được 401, và 401 thật vẫn phải mang header CORS — thiếu header thì trình duyệt báo "CORS error"
/// thay vì cho interceptor 401→refresh của FE đọc được mã 401.
/// </summary>
public sealed class CorsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string FrontendOrigin = "http://localhost:3000";

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Fact]
    public async Task Preflight_tu_frontend_204_dung_origin_va_cho_phep_credentials()
    {
        using var request = Preflight("/api/v1/auth/login", FrontendOrigin);

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(FrontendOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Preflight_tu_origin_la_khong_co_Access_Control_Allow_Origin()
    {
        using var request = Preflight("/api/v1/auth/login", "https://evil.example");

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    /// <summary>Preflight không mang token. UseCors đứng sau UseAuthorization thì fallback policy trả 401 cho chính preflight.</summary>
    [Fact]
    public async Task Preflight_toi_endpoint_doi_token_khong_bi_401()
    {
        using var request = Preflight("/api/v1/me", FrontendOrigin);

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(FrontendOrigin, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Response_401_that_van_mang_header_CORS_de_FE_doc_duoc_ma_loi()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Origin", FrontendOrigin);

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(FrontendOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
    }

    /// <summary>traceId trong ProblemDetails bằng header X-Correlation-ID — FE chỉ đọc được header đó khi server expose nó.</summary>
    [Fact]
    public async Task Request_that_expose_X_Correlation_ID()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
        request.Headers.Add("Origin", FrontendOrigin);

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FrontendOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("X-Correlation-ID", Header(response, "Access-Control-Expose-Headers") ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
