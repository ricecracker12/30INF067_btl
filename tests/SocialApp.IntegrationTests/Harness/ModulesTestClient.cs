using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// D0 (GĐ2): bọc <see cref="HttpClient"/> cho test endpoint của khối D. Đường dẫn và tên trường theo <c>profile-v1.yaml</c>
/// và <c>content-v1.yaml</c> — hai file hợp đồng, không theo trí nhớ.
///
/// Khác <c>AuthTestClient</c> của GĐ1 ở chỗ token: ở đây token do <see cref="TestJwt"/> ký thẳng với <c>userId</c> do test
/// chọn, không qua đăng ký + đăng nhập. Xem lý do ở <see cref="ModulesApiFactory"/> (Đ-2.2: không FK sang
/// <c>identity.users</c>). Nhờ vậy test dựng được "người dùng thứ hai" chỉ bằng một Guid mới.
///
/// Lớp này lớn dần theo từng đầu việc, KHÔNG dựng sẵn cả bộ ở D0: phương thức gọi endpoint chưa tồn tại thì không compile
/// được (DTO chưa có) và cũng không kiểm được điều gì. <c>PutProfileAsync</c> vào ở D2, <c>SetAvatarAsync</c> ở D3,
/// <c>CreatePostAsync</c> ở D5 — mỗi cái đi cùng commit của endpoint mà nó gọi.
/// </summary>
public sealed class ModulesTestClient
{
    private readonly ModulesApiFactory _factory;

    public ModulesTestClient(ModulesApiFactory factory)
    {
        _factory = factory;
        Http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Khối D không dùng cookie nào (token đi trong header Authorization). BaseAddress https để giống GĐ1 và để
            // cookie Secure của Identity — nếu test nào lỡ chạm /auth — không bị bỏ im lặng.
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    public HttpClient Http { get; }

    /// <summary>
    /// Header <c>Authorization</c> cho một người gọi. <paramref name="userId"/> chính là <c>sub</c>, tức là <c>actorId</c>
    /// mà <c>User.GetUserId()</c> đọc ra ở controller — test dựng "người khác" bằng một Guid khác, không cần dữ liệu nền.
    /// </summary>
    public static AuthenticationHeaderValue Bearer(Guid userId, string role = "USER") =>
        new("Bearer", TestJwt.Create(role, userId));

    /// <summary>
    /// Dựng "object đã nằm trong bucket" cho ba lớp của Đ-2.8 (D3, D5). Bọc <c>FakeObjectStorage.Put</c> để test không phải
    /// biết factory giữ fake ở đâu. KHÔNG tăng <c>HeadCalls</c> — chỉ lời gọi HEAD thật của code sản phẩm mới tăng, đó là
    /// cả lý do bộ đếm đó tồn tại.
    /// </summary>
    public void PutObject(string key, long sizeBytes, string contentType) =>
        _factory.Storage.Put(key, sizeBytes, contentType);

    /// <summary>
    /// Đọc Problem Details của một phản hồi lỗi. Trả <c>errors</c> đã phẳng thành <c>{field: [message]}</c> — rỗng khi phản
    /// hồi không phải 400 theo trường. Test khẳng định KEY và TITLE qua đây thay vì so nguyên chuỗi JSON: thêm một trường
    /// vào Problem Details (traceId, instance) không được làm đỏ test của endpoint.
    /// </summary>
    public static async Task<(int Status, string? Title, IReadOnlyDictionary<string, string[]> Errors)> ReadProblemAsync(
        HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (root.TryGetProperty("errors", out var node) && node.ValueKind == JsonValueKind.Object)
            foreach (var field in node.EnumerateObject())
                errors[field.Name] = field.Value.EnumerateArray().Select(m => m.GetString()!).ToArray();

        return (
            (int)response.StatusCode,
            root.TryGetProperty("title", out var title) ? title.GetString() : null,
            errors);
    }
}
