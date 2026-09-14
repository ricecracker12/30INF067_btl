using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using SetCookieHeaderValue = Microsoft.Net.Http.Headers.SetCookieHeaderValue;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// Bọc <see cref="HttpClient"/> cho test endpoint auth. Cookie refresh quản bằng TAY: RT-02 phải gửi lại cookie CŨ
/// sau khi server đã xoay — cookie jar tự động không cho làm vậy. Đường dẫn và tên trường theo identity-v1.yaml.
///
/// Test endpoint thật lấy token bằng <see cref="LoginAsync"/>, KHÔNG bằng TestJwt: TestJwt sinh <c>sub</c> ngẫu nhiên
/// không có trong DB. TestJwt chỉ dùng cho D8 (cần <c>iat</c> tùy ý).
/// </summary>
public sealed class AuthTestClient
{
    public const string RefreshCookieName = "refresh_token";

    /// <summary>Mật khẩu hợp lệ theo hợp đồng (8–72). Test cần mật khẩu sai thì tự ghép thêm.</summary>
    public const string Password = "MatKhauManh123";

    private readonly IdentityApiFactory _factory;

    public AuthTestClient(IdentityApiFactory factory)
    {
        _factory = factory;
        Http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,                          // tự quản cookie — xem summary
            BaseAddress = new Uri("https://localhost"),     // cookie Secure không đi qua http://
        });
    }

    public HttpClient Http { get; }

    /// <summary>Email ngẫu nhiên cho từng test — các test trong cùng lớp dùng chung database.</summary>
    public static string NewEmail() => $"u{Guid.NewGuid():N}@example.com";

    public Task<HttpResponseMessage> RegisterAsync(string email, string password) =>
        Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password });

    public Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        Http.PostAsJsonAsync("/api/v1/auth/verify-email", new { token });

    public Task<HttpResponseMessage> PostLoginAsync(string email, string password) =>
        Http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    /// <summary>Đăng ký + xác minh bằng token lấy từ <see cref="CapturingEmailSender"/>. Trả userId.</summary>
    public async Task<Guid> RegisterAndVerifyAsync(string email, string password)
    {
        using var registered = await RegisterAsync(email, password);
        await EnsureStatusAsync(registered, HttpStatusCode.Created);
        var body = await registered.Content.ReadFromJsonAsync<RegisterBody>();

        using var verified = await VerifyEmailAsync(_factory.Emails.LatestTokenFor(email));
        await EnsureStatusAsync(verified, HttpStatusCode.OK);

        return body!.UserId;
    }

    /// <summary>Đăng nhập thành công → access token trong body + GIÁ TRỊ cookie refresh (không kèm thuộc tính).</summary>
    public async Task<(string AccessToken, string RefreshCookie)> LoginAsync(string email, string password)
    {
        using var response = await PostLoginAsync(email, password);
        await EnsureStatusAsync(response, HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();

        var cookie = ReadSetCookie(response)
            ?? throw new InvalidOperationException($"Login 200 nhưng không có Set-Cookie {RefreshCookieName}.");
        return (body!.AccessToken, cookie.Value.ToString());
    }

    /// <summary>Không body (hợp đồng). <paramref name="refreshCookie"/> null = không gửi cookie.</summary>
    public Task<HttpResponseMessage> RefreshAsync(string? refreshCookie) =>
        SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", accessToken: null, refreshCookie);

    public Task<HttpResponseMessage> LogoutAsync(string? accessToken, string? refreshCookie) =>
        SendAsync(HttpMethod.Post, "/api/v1/auth/logout", accessToken, refreshCookie);

    public Task<HttpResponseMessage> GetMeAsync(string? accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/me", accessToken, refreshCookie: null);

    /// <summary>
    /// Set-Cookie tên <paramref name="name"/> (cái cuối nếu có nhiều), null nếu không có. Parser của chính ASP.NET Core:
    /// KHÔNG phân biệt hoa thường — ASP.NET ghi <c>path=</c>, <c>httponly</c>, <c>samesite=lax</c>.
    /// </summary>
    public static SetCookieHeaderValue? ReadSetCookie(HttpResponseMessage response, string name = RefreshCookieName) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList()).LastOrDefault(c => c.Name.Equals(name, StringComparison.Ordinal))
            : null;

    /// <summary>
    /// Sửa dữ liệu trực tiếp — dùng để lùi mốc thời gian (Đ-D10), KHÔNG thay TimeProvider trong test HTTP. Tham số vị
    /// trí <c>$1, $2…</c>. Trả số dòng bị ảnh hưởng.
    /// </summary>
    public async Task<int> ExecuteSqlAsync(string sql, params object[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Dòng đầu tiên dạng tên cột → giá trị (NULL thành <c>null</c>); <c>null</c> nếu không có dòng nào. Tham số như
    /// <see cref="ExecuteSqlAsync"/>. So cột <c>citext</c> với tham số chuỗi thì ép <c>$1::citext</c>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> QueryRowAsync(string sql, params object[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
        return row;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var value in parameters)
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        return command;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, string? refreshCookie)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (refreshCookie is not null)
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshCookie}");
        return await Http.SendAsync(request);
    }

    private static async Task EnsureStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
            throw new InvalidOperationException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: mong đợi "
              + $"{(int)expected}, nhận {(int)response.StatusCode}. Body: {await response.Content.ReadAsStringAsync()}");
    }

    private sealed record RegisterBody(Guid UserId, string Email);

    private sealed record TokenBody(string AccessToken, int ExpiresIn);
}
