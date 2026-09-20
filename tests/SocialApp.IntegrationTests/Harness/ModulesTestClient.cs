using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using SocialApp.SharedKernel.Storage;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Profile.Application.Profiles;

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
/// được (DTO chưa có) và cũng không kiểm được điều gì. <c>CreatePostAsync</c> vào ở D5 — mỗi cái đi cùng commit của
/// endpoint mà nó gọi.
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
    /// Tùy chọn đọc JSON cho DTO có enum. App ghi enum ra <b>chữ thường</b> (<c>"public"</c>, <c>"post"</c>) nhờ
    /// <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c> ở <c>Program.cs</c> — chốt Q-D2 — nên bản mặc định của
    /// <c>ReadFromJsonAsync</c> ném ngay ở <c>$.privacy</c>.
    ///
    /// Khai lại policy ở đây thay vì lấy từ app là CỐ Ý: test phải đọc được đúng thứ dây truyền đi, giống hệt type mà
    /// <c>openapi-typescript</c> sinh cho FE (union <c>'public' | 'friends' | 'private'</c>). Dùng chung
    /// <c>JsonSerializerOptions</c> với app thì app ghi PascalCase test vẫn xanh, và cổng hợp đồng không so schema của
    /// response nên CI cũng xanh — đúng loại lưới giả Q-D2 sinh ra để chặn.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
    /// <c>PUT /users/me/profile</c> với tư cách <paramref name="userId"/> (D2). Gửi body dạng ẩn danh thay vì
    /// <c>UpsertProfileRequest</c> để test bỏ hẳn được một trường — hợp đồng phân biệt "bio vắng mặt" với "bio null" ở
    /// mức JSON, và DTO thì luôn phát ra cả hai trường. Nhánh field lạ thì test tự dựng body, không qua đây.
    /// </summary>
    public Task<HttpResponseMessage> PutProfileAsync(Guid userId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/users/me/profile")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = Bearer(userId);
        return Http.SendAsync(request);
    }

    /// <summary>Như <see cref="PutProfileAsync"/> nhưng đọc luôn body 200. Dùng ở nhánh đã biết chắc là thành công.</summary>
    public async Task<ProfileResponse> PutProfileOkAsync(Guid userId, object body)
    {
        using var response = await PutProfileAsync(userId, body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
    }

    /// <summary><c>PUT /users/me/avatar</c> với tư cách <paramref name="userId"/> (D3).</summary>
    public Task<HttpResponseMessage> SetAvatarAsync(Guid userId, string mediaKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/users/me/avatar")
        {
            Content = JsonContent.Create(new { mediaKey }),
        };
        request.Headers.Authorization = Bearer(userId);
        return Http.SendAsync(request);
    }

    /// <summary><c>DELETE /users/me/avatar</c> với tư cách <paramref name="userId"/> (D3).</summary>
    public Task<HttpResponseMessage> RemoveAvatarAsync(Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me/avatar");
        request.Headers.Authorization = Bearer(userId);
        return Http.SendAsync(request);
    }

    /// <summary>
    /// Dựng một avatar ĐÃ tải lên đúng chuẩn cho <paramref name="userId"/> và trả key. Dùng
    /// <c>StorageKeys.ForAvatar</c> của code sản phẩm để sinh key thay vì ghép chuỗi trong test: key gõ tay mà lệch dạng
    /// thì test đỏ ở lớp 1 (regex) và không bao giờ chạm tới lớp mình định kiểm.
    /// </summary>
    public string PutAvatarObject(Guid userId, string contentType = "image/jpeg", long sizeBytes = 1024)
    {
        var key = StorageKeys.ForAvatar(userId, contentType);
        PutObject(key, sizeBytes, contentType);
        return key;
    }

    /// <summary>
    /// <c>POST /media/uploads</c> với tư cách <paramref name="userId"/> (D4). Nhận <paramref name="body"/> dạng ẩn danh
    /// chứ không nhận <c>CreateUploadsRequest</c>: test phải gửi được <c>purpose</c> vắng mặt, <c>purpose</c> lạ
    /// (<c>"everyone"</c>) và <c>files: null</c> — những thứ DTO đã gõ kiểu thì không phát ra nổi.
    ///
    /// <paramref name="role"/> mở ra để kiểm hai mức quyền của Đ-2.6: <c>"GUEST"</c> là vai trò KHÔNG có dòng nào trong
    /// <c>identity.role_permissions</c>, nên nó thiếu <c>post.create</c> mà không phải xóa dữ liệu của ai.
    /// </summary>
    public Task<HttpResponseMessage> CreateUploadsAsync(Guid userId, object body, string role = "USER")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media/uploads")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = Bearer(userId, role);
        return Http.SendAsync(request);
    }

    /// <summary>
    /// <c>POST /posts</c> với tư cách <paramref name="userId"/> (D5). Nhận <paramref name="body"/> dạng ẩn danh vì test
    /// phải gửi được <c>privacy</c> vắng mặt, field lạ, và <c>mediaKeys</c> sai dạng — những thứ DTO đã gõ kiểu thì
    /// không phát ra nổi.
    ///
    /// <paramref name="role"/> và <paramref name="token"/> mở ra cho hai nhánh riêng: vai trò thiếu <c>post.create</c>
    /// (tầng 2) và token hết hạn (<c>AC-04</c>, tầng 1).
    /// </summary>
    public Task<HttpResponseMessage> CreatePostAsync(Guid userId, object body, string role = "USER", string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = token is null
            ? Bearer(userId, role)
            : new AuthenticationHeaderValue("Bearer", token);
        return Http.SendAsync(request);
    }

    /// <summary>Như <see cref="CreatePostAsync"/> nhưng đọc luôn body 201. Dùng ở nhánh đã biết chắc là thành công.</summary>
    public async Task<PostResponse> CreatePostOkAsync(Guid userId, object body)
    {
        using var response = await CreatePostAsync(userId, body);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PostResponse>(Json))!;
    }

    /// <summary>
    /// Dựng một ảnh bài ĐÃ tải lên đúng chuẩn cho <paramref name="userId"/> và trả về khai báo để gửi trong
    /// <c>mediaKeys</c>. Dùng <c>StorageKeys.ForPost</c> của code sản phẩm thay vì ghép chuỗi trong test: key gõ tay mà
    /// lệch dạng thì test đỏ ở lớp regex và không bao giờ chạm tới lớp mình định kiểm (bài học từ D3).
    /// </summary>
    public object PutPostObject(Guid userId, string contentType = "image/jpeg", long sizeBytes = 1024)
    {
        var key = StorageKeys.ForPost(userId, contentType);
        PutObject(key, sizeBytes, contentType);
        return new { mediaKey = key, contentType, sizeBytes };
    }

    /// <summary><c>GET /posts/{postId}</c> với tư cách <paramref name="userId"/> (D6).</summary>
    public Task<HttpResponseMessage> GetPostAsync(Guid userId, object postId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/posts/{postId}");
        request.Headers.Authorization = Bearer(userId);
        return Http.SendAsync(request);
    }

    /// <summary>
    /// <c>GET /users/{userId}/posts</c> với tư cách <paramref name="actorId"/> (D6). <paramref name="query"/> nhận
    /// nguyên chuỗi query (đã có dấu <c>?</c>) để test gửi được cursor rác và limit sai kiểu — những thứ tham số đã gõ
    /// kiểu không phát ra nổi.
    /// </summary>
    public Task<HttpResponseMessage> ListPostsAsync(Guid actorId, Guid authorId, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{authorId:D}/posts{query}");
        request.Headers.Authorization = Bearer(actorId);
        return Http.SendAsync(request);
    }

    /// <summary>Như <see cref="ListPostsAsync"/> nhưng đọc luôn body 200.</summary>
    public async Task<PostPage> ListPostsOkAsync(Guid actorId, Guid authorId, string query = "")
    {
        using var response = await ListPostsAsync(actorId, authorId, query);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PostPage>(Json))!;
    }

    /// <summary>
    /// <c>PATCH /posts/{postId}</c> với tư cách <paramref name="userId"/> (D7). Nhận <paramref name="body"/> dạng ẩn
    /// danh vì test phải gửi được <c>{}</c>, field lạ (<c>mediaKeys</c>) và <c>body: ""</c> — những thứ DTO đã gõ kiểu
    /// không phát ra nổi.
    /// </summary>
    public Task<HttpResponseMessage> UpdatePostAsync(Guid userId, object postId, object body, string role = "USER")
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/posts/{postId}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = Bearer(userId, role);
        return Http.SendAsync(request);
    }

    /// <summary>Như <see cref="UpdatePostAsync"/> nhưng đọc luôn body 200.</summary>
    public async Task<PostResponse> UpdatePostOkAsync(Guid userId, Guid postId, object body)
    {
        using var response = await UpdatePostAsync(userId, postId, body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PostResponse>(Json))!;
    }

    /// <summary><c>DELETE /posts/{postId}</c> với tư cách <paramref name="userId"/> (D8).</summary>
    public Task<HttpResponseMessage> DeletePostAsync(Guid userId, object postId, string role = "USER")
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/posts/{postId}");
        request.Headers.Authorization = Bearer(userId, role);
        return Http.SendAsync(request);
    }

    /// <summary>
    /// Đọc thẳng DB bằng Npgsql — KHÔNG qua EF và không qua API. Chép khuôn <c>AuthTestClient.QueryRowAsync</c> của GĐ1.
    /// Cần cho <c>PROF-02</c>: "vẫn đúng MỘT dòng" là khẳng định về bảng, mà API thì theo thiết kế không phân biệt được
    /// một dòng với hai dòng. Tham số vị trí <c>$1, $2…</c>; NULL thành <c>null</c>; <c>null</c> nếu không có dòng nào.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> QueryRowAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in parameters)
            command.Parameters.Add(new NpgsqlParameter { Value = value });

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
        return row;
    }

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
