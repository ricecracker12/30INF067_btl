using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application;

namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// GĐ6 D1 — phần login/refresh của <c>ADM-01</c> (Đ-6.5): tài khoản <c>disabled</c> không lấy được token mới bằng mật khẩu lẫn
/// bằng refresh. Endpoint khóa là D3; ở đây đặt <c>status</c> bằng SQL (ngoại lệ luật 9 như <c>FEED-06</c>) và — ở hai ca refresh —
/// CỐ Ý không thu hồi family, để thử đúng lưới thứ hai trong <c>RotateAsync</c> chứ không phải lưới "family đã bị thu hồi" của D3.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountDisabledTests(PostgresFixture postgres, IdentityApiFactory factory)
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
    public async Task ADM_01_login_dung_mat_khau_tren_tai_khoan_bi_khoa_403_account_disabled_khong_phat_token()
    {
        var (userId, email) = await VerifiedUserAsync();
        await DisableAsync(userId);

        using var response = await _auth.PostLoginAsync(email, AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(IdentityErrors.AccountDisabledType, problem.GetProperty("type").GetString());
        Assert.Equal("Bị từ chối", problem.GetProperty("title").GetString());
        Assert.Equal("Tài khoản đã bị khóa. Liên hệ quản trị viên.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));

        Assert.Null(AuthTestClient.ReadSetCookie(response));
        Assert.Equal(0L, await RefreshTokenCountAsync(userId));
    }

    /// <summary>Không biết mật khẩu thì không biết tài khoản bị khóa: thân 401 GIỐNG HỆT email không tồn tại (AC-02).</summary>
    [Fact]
    public async Task ADM_01_login_sai_mat_khau_tren_tai_khoan_bi_khoa_401_cung_than_voi_email_khong_ton_tai()
    {
        var (userId, email) = await VerifiedUserAsync();
        await DisableAsync(userId);

        using var disabledWrong = await _auth.PostLoginAsync(email, AuthTestClient.Password + "sai");
        using var unknownEmail = await _auth.PostLoginAsync(AuthTestClient.NewEmail(), AuthTestClient.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, disabledWrong.StatusCode);
        Assert.Equal(await WithoutPerRequestFieldsAsync(unknownEmail), await WithoutPerRequestFieldsAsync(disabledWrong));
    }

    /// <summary>
    /// Lưới thứ hai của Đ-6.5: family CÒN SỐNG (không ai thu hồi) mà tài khoản đã <c>disabled</c> → 401, và KHÔNG có dòng
    /// <c>refresh_tokens</c> nào được chèn — kiểm trạng thái phải đứng TRƯỚC bước xoay (L-D2).
    /// </summary>
    [Fact]
    public async Task ADM_01_refresh_tren_tai_khoan_bi_khoa_401_khong_xoay_khong_chen_token()
    {
        var (userId, email) = await VerifiedUserAsync();
        var (_, cookie) = await _auth.LoginAsync(email, AuthTestClient.Password);
        await DisableAsync(userId);

        using var response = await _auth.RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1L, await RefreshTokenCountAsync(userId));
        var row = await _auth.QueryRowAsync(
            "SELECT revoked_at, replaced_by_id FROM identity.refresh_tokens WHERE token_hash = $1", Sha256Hex(cookie));
        Assert.Null(row!["revoked_at"]);        // không xoay — token cũ không bị đánh dấu
        Assert.Null(row["replaced_by_id"]);
    }

    /// <summary>
    /// Nhánh ân hạn 3a (Đ-D3) cũng PHÁT token (anh em cùng family) — cùng lưới. Token vừa bị xoay dùng lại ngay (trong 10 giây)
    /// sau khi tài khoản bị khóa → 401, family không thêm dòng.
    /// </summary>
    [Fact]
    public async Task ADM_01_refresh_trong_an_han_tren_tai_khoan_bi_khoa_401_khong_phat_anh_em()
    {
        var (userId, email) = await VerifiedUserAsync();
        var (_, first) = await _auth.LoginAsync(email, AuthTestClient.Password);
        using (var rotated = await _auth.RefreshAsync(first))
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        await DisableAsync(userId);

        using var response = await _auth.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2L, await RefreshTokenCountAsync(userId));
    }

    /// <summary>Đối chứng: tài khoản <c>active</c> vẫn refresh được — lưới mới không chặn nhầm.</summary>
    [Fact]
    public async Task Tai_khoan_active_refresh_van_200()
    {
        var (_, email) = await VerifiedUserAsync();
        var (_, cookie) = await _auth.LoginAsync(email, AuthTestClient.Password);

        using var response = await _auth.RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Guid UserId, string Email)> VerifiedUserAsync()
    {
        var email = AuthTestClient.NewEmail();
        return (await _auth.RegisterAndVerifyAsync(email, AuthTestClient.Password), email);
    }

    private Task<int> DisableAsync(Guid userId) =>
        _auth.ExecuteSqlAsync("UPDATE identity.users SET status = 'disabled' WHERE user_id = $1", userId);

    private async Task<long> RefreshTokenCountAsync(Guid userId) =>
        (long)(await _auth.QueryRowAsync(
            "SELECT count(*) AS n FROM identity.refresh_tokens WHERE user_id = $1", userId))!["n"]!;

    private static string Sha256Hex(string plain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();

    private static async Task<string> WithoutPerRequestFieldsAsync(HttpResponseMessage response)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }
}
