using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests;

/// <summary>
/// D9 — MỘT hình dạng lỗi cho mọi nguồn sinh Problem Details: status code pages (401 thiếu token, 404/405, 429 rate limit),
/// validation 400 (FluentValidation + body hỏng ở tầng JSON), 415 của input formatter, và 500 của GlobalExceptionHandler. Hợp đồng
/// <c>required: [title, status, traceId]</c>, <c>type</c> mặc định <c>https://httpstatuses.io/{status}</c>. Title và thông điệp
/// viết tay theo identity-v1.yaml / SharedKernel. Không chạm DB/Redis (<see cref="ApiFactory"/>).
///
/// <b>Một case của lớp này sống ở nơi khác:</b> 400 sinh từ <c>Error.Validation</c> trong service SAU I/O (Q-D4) —
/// nguồn sinh thứ sáu, và là nguồn duy nhất cần DB, nên nó nằm ở
/// <c>Profile.AvatarTests.D9_400_sinh_tu_Error_Validation_co_cung_hinh_dang_voi_400_cua_FluentValidation</c>. Lớp này
/// cố ý giữ nguyên tắc "không chạm DB" để chạy được cả khi Postgres không lên.
/// </summary>
public sealed class ProblemDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Login = "/api/v1/auth/login";

    [Fact]
    public async Task Thieu_token_401_title_Chua_xac_thuc()
    {
        using var response = await factory.CreateClient().GetAsync("/api/v1/me");

        var problem = await AssertProblemAsync(response, 401, "Chưa xác thực");
        Assert.Equal("/api/v1/me", problem.GetProperty("instance").GetString());
    }

    /// <summary>
    /// Token hỏng vì lý do gì cũng CÙNG một 401, header không nêu lý do: mặc định JwtBearer ghi error_description ("The signature key
    /// was not found", "The token expired at '…'"), cho kẻ dò token biết token nó cầm hỏng ở đâu.
    /// </summary>
    [Fact]
    public async Task Token_sai_chu_ky_hoac_het_han_401_header_khong_neu_ly_do()
    {
        var tokens = new Dictionary<string, string>
        {
            ["sai chữ ký"] = TestJwt.Create("USER", signingKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
            ["hết hạn"] = TestJwt.Create("USER", issuedAt: DateTimeOffset.UtcNow.AddHours(-1)),
        };

        foreach (var (name, token) in tokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await factory.CreateClient().SendAsync(request);

            await AssertProblemAsync(response, 401, "Chưa xác thực");
            var challenge = string.Join(", ", response.Headers.WwwAuthenticate);
            Assert.True(challenge == "Bearer", $"{name}: WWW-Authenticate lộ lý do: {challenge}");
        }
    }

    [Fact]
    public async Task Route_khong_ton_tai_co_token_404_title_Khong_tim_thay()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/__khong-ton-tai", bearer: true);

        await AssertProblemAsync(response, 404, "Không tìm thấy tài nguyên");
    }

    /// <summary>
    /// QUYẾT ĐỊNH CÓ Ý THỨC (D9): gọi ẩn danh sai method hoặc route lạ nhận CÙNG 401 như endpoint cần đăng nhập — bước chọn endpoint
    /// không cho biết endpoint đích có công khai không, và trả 405/404 cho người chưa đăng nhập là cho dò được route nào tồn tại.
    /// Đổi hành vi này phải sửa test cùng lúc.
    /// </summary>
    [Fact]
    public async Task An_danh_sai_method_va_route_la_deu_401_khong_lo_route()
    {
        using var wrongMethod = await SendAsync(HttpMethod.Get, Login, bearer: false);
        using var unknownRoute = await SendAsync(HttpMethod.Get, "/api/v1/__khong-ton-tai", bearer: false);

        await AssertProblemAsync(wrongMethod, 401, "Chưa xác thực");
        await AssertProblemAsync(unknownRoute, 401, "Chưa xác thực");
    }

    [Fact]
    public async Task Co_token_sai_method_405_title_va_header_Allow()
    {
        using var response = await SendAsync(HttpMethod.Get, Login, bearer: true);

        await AssertProblemAsync(response, 405, "Phương thức không được hỗ trợ");
        Assert.Contains("POST", response.Content.Headers.Allow);
    }

    /// <summary>Endpoint công khai, gọi ẩn danh, sai content type → 415 thật (trước D9: 401 vì [Consumes] loại action lúc chọn endpoint).</summary>
    [Fact]
    public async Task An_danh_sai_content_type_toi_endpoint_cong_khai_415()
    {
        using var content = new StringContent("email=an@example.com", Encoding.UTF8, "text/plain");

        using var response = await factory.CreateClient().PostAsync(Login, content);

        await AssertProblemAsync(response, 415, "Kiểu nội dung không được hỗ trợ");
    }

    /// <summary>
    /// Body hỏng ở mọi tầng: KHÔNG lộ tên kiểu .NET, vị trí byte hay thông điệp tiếng Anh của System.Text.Json; key là tên trường
    /// (không <c>$…</c>, không rỗng); thiếu trường đi tới FluentValidation. Bảng chuỗi viết tay, không đọc hằng số của SharedKernel.
    /// </summary>
    public static TheoryData<string, string, string, string> BrokenBodies => new()
    {
        { "body rỗng", "", "body", "Thiếu nội dung yêu cầu." },
        { "body null", "null", "body", "Thiếu nội dung yêu cầu." },
        { "body là mảng", "[]", "body", "Nội dung yêu cầu không đúng định dạng JSON." },
        { "không phải JSON", "khong-phai-json", "body", "Nội dung yêu cầu không đúng định dạng JSON." },
        { "field lạ", """{"email":"an@example.com","password":"MatKhauManh123","role":"ADMIN"}""", "role",
            "Giá trị không hợp lệ hoặc trường không được hỗ trợ." },
        { "field lạ lồng nhau", """{"email":"an@example.com","password":"MatKhauManh123","extra":{"x":1}}""", "extra",
            "Giá trị không hợp lệ hoặc trường không được hỗ trợ." },
        { "sai kiểu", """{"email":"an@example.com","password":123}""", "password",
            "Giá trị không hợp lệ hoặc trường không được hỗ trợ." },
        { "object rỗng", "{}", "email", "Email là bắt buộc." },
        { "thiếu password", """{"email":"an@example.com"}""", "password", "Mật khẩu là bắt buộc." },
    };

    [Theory]
    [MemberData(nameof(BrokenBodies))]
    public async Task Body_hong_400_khong_lo_chi_tiet_noi_bo(string name, string body, string field, string message)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await factory.CreateClient().PostAsync(Login, content);

        var problem = await AssertProblemAsync(response, 400, "Dữ liệu không hợp lệ");
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", problem.GetProperty("detail").GetString());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out var messages), $"{name}: errors không có key '{field}': {errors}");
        Assert.Contains(message, messages.EnumerateArray().Select(m => m.GetString()));
        Assert.All(errors.EnumerateObject(), e => Assert.False(e.Name.Length == 0 || e.Name.StartsWith('$'), $"{name}: key '{e.Name}'"));

        var raw = problem.GetRawText();
        foreach (var leak in new[] { "SocialApp.", "System.", "LineNumber", "BytePosition", "JSON value", "could not", "required" })
            Assert.False(raw.Contains(leak, StringComparison.Ordinal), $"{name}: response lộ \"{leak}\": {raw}");
    }

    /// <summary>App riêng + CÙNG một IP (header của FakeRemoteIpStartupFilter): hạn mức 10 req/phút/IP tính theo từng app.</summary>
    [Fact]
    public async Task Vuot_rate_limit_429_title_Qua_nhieu_yeu_cau()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        HttpResponseMessage? last = null;
        for (var attempt = 1; attempt <= 11; attempt++)
        {
            last?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Post, Login)
            {
                Content = JsonContent.Create(new { email = "", password = "" }),
            };
            request.Headers.Add(FakeRemoteIpStartupFilter.Header, "203.0.113.50");
            last = await client.SendAsync(request);
        }

        using (last)
            await AssertProblemAsync(last!, 429, "Quá nhiều yêu cầu");
    }

    /// <summary>500 KHÔNG có detail — GlobalExceptionHandler giấu chi tiết nội bộ (hợp đồng InternalError).</summary>
    [Fact]
    public async Task Loi_khong_mong_muon_500_title_va_khong_co_detail()
    {
        using var response = await factory.CreateClient().GetAsync("/api/v1/ping/boom");

        var problem = await AssertProblemAsync(response, 500, "Đã xảy ra lỗi không mong muốn");
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind != JsonValueKind.Null,
            $"500 lộ detail: {problem}");
    }

    /// <summary>
    /// GĐ6 D5 (L-D11): <c>Error.Extensions</c> lên thân Problem Details đúng key, đúng giá trị — VẪN có <c>traceId</c> khớp header,
    /// <c>type</c> riêng, <c>instance</c>, content type <c>problem+json</c>: cùng factory với mọi lỗi khác, không phải chỗ dựng thứ hai.
    /// </summary>
    [Fact]
    public async Task Error_co_Extensions_len_than_loi_van_co_traceId_va_type()
    {
        await using var app = new ApiFactory();
        using var probed = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddControllers().AddApplicationPart(typeof(ProblemProbeController).Assembly)));

        using var response = await probed.CreateClient().GetAsync("/__test/problem/extensions");

        Assert.Equal(409, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:socialapp:problem:probe", problem.GetProperty("type").GetString());
        Assert.Equal("Cần xác nhận", problem.GetProperty("title").GetString());
        Assert.Equal("Cần xác nhận.", problem.GetProperty("detail").GetString());
        Assert.Equal("/__test/problem/extensions", problem.GetProperty("instance").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), problem.GetProperty("traceId").GetString());
        Assert.Equal(["a.b"], problem.GetProperty("added").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, problem.GetProperty("removed").GetArrayLength());
        Assert.Equal(3, problem.GetProperty("affectedUsers").GetInt32());
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, bool bearer)
    {
        using var request = new HttpRequestMessage(method, path);
        if (bearer)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("USER"));
        return await factory.CreateClient().SendAsync(request);
    }

    /// <summary>Hình dạng chung: status, title, type theo hợp đồng, traceId == header X-Correlation-ID của chính response.</summary>
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status, string title)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal($"https://httpstatuses.io/{status}", problem.GetProperty("type").GetString());
        var traceId = problem.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), traceId);
        return problem;
    }
}
