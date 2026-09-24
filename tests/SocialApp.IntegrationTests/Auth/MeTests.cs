using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// D7 — <c>GET /me</c> trên Postgres thật, token lấy bằng đăng nhập thật (không TestJwt: sub ngẫu nhiên không có trong DB).
/// Kỳ vọng viết tay theo hợp đồng (<c>MeResponse</c>) và Mục 5.1 (<c>"USER"</c> / <c>"Người dùng"</c>). "Không token → 401" là
/// dòng <c>TC-A01-me</c> của AuthZ matrix, không lặp lại ở đây.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MeTests(PostgresFixture postgres, IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private AuthTestClient _auth = null!;

    public async Task InitializeAsync()
    {
        await factory.UseFreshDatabaseAsync(postgres);
        _auth = new AuthTestClient(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dang_nhap_roi_goi_me_200_dung_8_truong_doc_tu_DB()
    {
        var (userId, email, accessToken) = await LoggedInUserAsync();

        using var response = await _auth.GetMeAsync(accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Đúng tập trường của hợp đồng — không passwordHash, không cột nào khác của users lọt ra. `permissions` thêm ở GĐ6 D1.
        Assert.Equal(
            ["createdAt", "email", "emailVerifiedAt", "permissions", "role", "roleDisplayName", "status", "userId"],
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(userId, body.GetProperty("userId").GetGuid());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("USER", body.GetProperty("role").GetString());
        Assert.Equal("Người dùng", body.GetProperty("roleDisplayName").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("emailVerifiedAt").ValueKind);
        Assert.InRange(body.GetProperty("createdAt").GetDateTimeOffset(),
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    /// <summary>Quyết định 3: roles tách code (bất biến) và display_name (sửa được). /me đọc cả hai từ DB.</summary>
    [Fact]
    public async Task Doi_display_name_bang_SQL_me_tra_ten_moi_role_van_USER()
    {
        var (_, _, accessToken) = await LoggedInUserAsync();

        await _auth.ExecuteSqlAsync("UPDATE identity.roles SET display_name = $1 WHERE code = 'USER'", "Thành viên");
        try
        {
            using var response = await _auth.GetMeAsync(accessToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Thành viên", body.GetProperty("roleDisplayName").GetString());
            Assert.Equal("USER", body.GetProperty("role").GetString());
        }
        finally
        {
            // Các test khác trong lớp dùng chung database và kỳ vọng "Người dùng".
            await _auth.ExecuteSqlAsync("UPDATE identity.roles SET display_name = $1 WHERE code = 'USER'", "Người dùng");
        }
    }

    /// <summary>Token đúng chữ ký nhưng user không còn: 401 (hợp đồng /me chỉ có 200/401), không 404, không 500.</summary>
    [Fact]
    public async Task User_da_bi_xoa_token_cu_401_problem_json()
    {
        var (userId, _, accessToken) = await LoggedInUserAsync();
        await _auth.ExecuteSqlAsync("DELETE FROM identity.users WHERE user_id = $1", userId);

        using var response = await _auth.GetMeAsync(accessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("Phiên không hợp lệ", problem.GetProperty("title").GetString());
    }

    /// <summary>
    /// /me dùng hạn mức chung theo user, KHÔNG phải policy "auth" 10 req/phút/IP. Gắn nhầm thì FE gọi /me vài lần khi tải
    /// trang là 429. Cùng một IP qua header của <see cref="FakeRemoteIpStartupFilter"/> để policy "auth" (nếu có) cắn được.
    /// </summary>
    [Fact]
    public async Task Me_khong_dung_han_muc_auth_11_request_cung_IP_deu_200()
    {
        var (_, _, accessToken) = await LoggedInUserAsync();
        var ip = $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";

        for (var attempt = 1; attempt <= 11; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add(FakeRemoteIpStartupFilter.Header, ip);
            using var response = await _auth.Http.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"request {attempt}: nhận {(int)response.StatusCode}");
        }
    }

    /// <summary>
    /// <c>ME-01</c> (GĐ6 D1, Đ-6.11) — quyền HIỆU LỰC đọc từ DB theo vai trò hiện tại. Kỳ vọng viết tay theo bảng bootstrap của
    /// giai-doan-1.md Mục 5.3, thứ tự <c>permission_id</c>. Đổi vai trò bằng SQL: <c>/me</c> đọc DB, không đọc claim, nên token
    /// cũ (role = USER) vẫn thấy tập mới — đúng thứ FE cần khi nạp lại <c>/me</c> lúc tab lấy lại focus.
    /// </summary>
    [Theory]
    [InlineData("USER", new[]
    {
        "post.read.public", "post.read.friends", "post.create", "post.update", "post.delete", "comment.create",
        "reaction.set", "friend.request", "friend.respond", "message.send", "report.create",
    })]
    [InlineData("MODERATOR", new[]
    {
        "post.read.public", "post.read.friends", "post.create", "post.update", "post.delete", "post.hide", "comment.create",
        "reaction.set", "friend.request", "friend.respond", "message.send", "report.create", "report.resolve",
    })]
    [InlineData("ADMIN", new[]
    {
        "post.read.public", "post.read.friends", "post.create", "post.update", "post.delete", "post.hide", "comment.create",
        "reaction.set", "friend.request", "friend.respond", "message.send", "report.create", "report.resolve",
        "user.lock", "user.unlock", "role.assign", "audit.read", "role.manage",
    })]
    public async Task ME_01_permissions_la_quyen_hieu_luc_cua_vai_tro_doc_tu_DB(string roleCode, string[] expected)
    {
        var (userId, _, accessToken) = await LoggedInUserAsync();
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.users SET role_id = (SELECT role_id FROM identity.roles WHERE code = $1) WHERE user_id = $2",
            roleCode, userId);

        var body = await MeBodyAsync(accessToken);

        Assert.Equal(roleCode, body.GetProperty("role").GetString());
        Assert.Equal(expected, Permissions(body));
    }

    /// <summary>
    /// <c>ME-01</c> vế vai trò tự tạo — thứ Đ-6.11 sinh ra để phục vụ: <c>REVIEWER</c> không có tên hệ thống nào, FE chỉ biết nó
    /// được vào hàng đợi nhờ <c>permissions</c>. Hợp đồng <c>RoleCode</c> nới thành chuỗi mở (L-D16). Tạo bằng SQL — API vai trò
    /// là D5 — lấy id từ sequence của A3.
    /// </summary>
    [Fact]
    public async Task ME_01_vai_tro_tu_tao_tra_dung_ma_va_dung_tap_quyen()
    {
        var (userId, _, accessToken) = await LoggedInUserAsync();
        var code = $"REVIEWER_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        await _auth.ExecuteSqlAsync("""
            WITH r AS (
                INSERT INTO identity.roles (role_id, code, display_name)
                VALUES (nextval('identity.roles_role_id_seq'), $1, 'Người xem xét') RETURNING role_id)
            INSERT INTO identity.role_permissions (role_id, permission_id)
            SELECT r.role_id, p.permission_id FROM r, identity.permissions p WHERE p.code IN ('report.resolve', 'report.create')
            """, code);
        await _auth.ExecuteSqlAsync(
            "UPDATE identity.users SET role_id = (SELECT role_id FROM identity.roles WHERE code = $1) WHERE user_id = $2",
            code, userId);

        var body = await MeBodyAsync(accessToken);

        Assert.Equal(code, body.GetProperty("role").GetString());
        Assert.Equal("Người xem xét", body.GetProperty("roleDisplayName").GetString());
        Assert.Equal(["report.create", "report.resolve"], Permissions(body));
    }

    private async Task<JsonElement> MeBodyAsync(string accessToken)
    {
        using var response = await _auth.GetMeAsync(accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] Permissions(JsonElement body) =>
        [.. body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];

    private async Task<(Guid UserId, string Email, string AccessToken)> LoggedInUserAsync()
    {
        var email = AuthTestClient.NewEmail();
        var userId = await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password);
        var (accessToken, _) = await _auth.LoginAsync(email, AuthTestClient.Password);
        return (userId, email, accessToken);
    }
}
