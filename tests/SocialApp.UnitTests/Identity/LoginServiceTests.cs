using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Application.Login;
using SocialApp.Modules.Identity.Application.Me;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Authentication;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Thứ tự 6 bước của giai-doan-1.md Mục 7.2, không dựng DB: store, hasher, issuer giả đếm lời gọi. Lưới CHÍNH của AC-02 là
/// test đầu tiên — nhánh email không tồn tại phải tốn một phép BCrypt; AC-02c (đo thời gian) chỉ là lưới phụ.
/// </summary>
public sealed class LoginServiceTests
{
    private const string CorrectPassword = "MatKhauManh123";
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly IPAddress Ip = IPAddress.Parse("203.0.113.7");

    private readonly FakeUsers _users = new();
    private readonly FakeRefreshTokens _refreshTokens = new();
    private readonly FakeHasher _hasher = new();
    private readonly FakeIssuer _issuer = new();

    private LoginService Service() => new(
        _users, _refreshTokens, _hasher, _issuer,
        Options.Create(new JwtOptions { RefreshTokenDays = 7 }), new FixedTime(Now), NullLogger<LoginService>.Instance);

    private Task<SocialApp.SharedKernel.Results.Result<LoginSuccess>> LoginAsync(string password = CorrectPassword) =>
        Service().LoginAsync(new LoginRequest { Email = "  an@example.com ", Password = password }, Ip, CancellationToken.None);

    private static LoginCandidate Candidate(DateTimeOffset? verifiedAt = default, DateTimeOffset? lockedUntil = null, bool verified = true) =>
        new(Guid.NewGuid(), "hash-that", "USER", verified ? verifiedAt ?? Now.AddDays(-1) : null, lockedUntil);

    [Fact]
    public async Task Email_khong_ton_tai_van_goi_VerifyAgainstDummy_dung_mot_lan()
    {
        _users.Candidate = null;

        var result = await LoginAsync();

        Assert.Equal(IdentityErrors.InvalidCredentials, result.Error);
        Assert.Equal(1, _hasher.DummyCalls);
        Assert.Equal(0, _hasher.VerifyCalls);
        Assert.Equal(0, _users.FailedCalls);
        Assert.Equal("an@example.com", _users.LastEmail);   // email được Trim trước khi tra
    }

    [Fact]
    public async Task Sai_mat_khau_tang_bo_dem_va_tra_CUNG_loi_voi_email_khong_ton_tai()
    {
        _users.Candidate = Candidate();

        var result = await LoginAsync("sai-mat-khau");

        Assert.Equal(IdentityErrors.InvalidCredentials, result.Error);
        Assert.Equal(1, _users.FailedCalls);
        Assert.Equal(0, _users.ResetCalls);
        Assert.Empty(_refreshTokens.Created);
        Assert.Empty(_issuer.Calls);
    }

    [Fact]
    public async Task Dang_khoa_423_khong_kiem_mat_khau_khong_tang_bo_dem()
    {
        _users.Candidate = Candidate(lockedUntil: Now.AddSeconds(1));

        var result = await LoginAsync();

        Assert.Equal(IdentityErrors.Locked, result.Error);
        Assert.Equal(0, _hasher.VerifyCalls);
        Assert.Equal(0, _users.FailedCalls);
        Assert.Empty(_issuer.Calls);
    }

    [Fact]
    public async Task Khoa_da_het_han_dang_nhap_dung_thanh_cong()
    {
        _users.Candidate = Candidate(lockedUntil: Now.AddSeconds(-1));

        var result = await LoginAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _users.ResetCalls);
    }

    /// <summary>Bước 4 đứng trước bước 5: chưa xác minh mà sai mật khẩu vẫn là 401 và vẫn tăng bộ đếm.</summary>
    [Fact]
    public async Task Chua_xac_minh_sai_mat_khau_401_va_tang_bo_dem()
    {
        _users.Candidate = Candidate(verified: false);

        var result = await LoginAsync("sai-mat-khau");

        Assert.Equal(IdentityErrors.InvalidCredentials, result.Error);
        Assert.Equal(1, _users.FailedCalls);
    }

    [Fact]
    public async Task Chua_xac_minh_dung_mat_khau_403_khong_reset_khong_phat_token()
    {
        _users.Candidate = Candidate(verified: false);

        var result = await LoginAsync();

        Assert.Equal(IdentityErrors.EmailNotVerified, result.Error);
        Assert.Equal(0, _users.ResetCalls);
        Assert.Empty(_refreshTokens.Created);
        Assert.Empty(_issuer.Calls);
    }

    [Fact]
    public async Task Thanh_cong_reset_luu_bam_refresh_han_7_ngay_phat_access_theo_role_code()
    {
        var user = Candidate();
        _users.Candidate = user;

        var result = await LoginAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _users.ResetCalls);

        var created = Assert.Single(_refreshTokens.Created);
        Assert.Equal(user.UserId, created.UserId);
        Assert.NotEqual(Guid.Empty, created.FamilyId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.Value!.RefreshPlain))).ToLowerInvariant(), created.TokenHash);
        Assert.Equal(Now.AddDays(7), created.ExpiresAt);
        Assert.Equal(Ip, created.Ip);
        Assert.Equal(Now, created.CreatedAt);

        Assert.Equal((user.UserId, "USER"), Assert.Single(_issuer.Calls));
        Assert.Equal("token-gia", result.Value.Access.Token);
    }

    [Fact]
    public async Task Hai_lan_dang_nhap_hai_family_khac_nhau()
    {
        _users.Candidate = Candidate();

        await LoginAsync();
        await LoginAsync();

        Assert.Equal(2, _refreshTokens.Created.Select(c => c.FamilyId).Distinct().Count());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeUsers : IIdentityUserStore
    {
        public LoginCandidate? Candidate { get; set; }
        public string? LastEmail { get; private set; }
        public int FailedCalls { get; private set; }
        public int ResetCalls { get; private set; }

        public Task<LoginCandidate?> FindForLoginAsync(string email, CancellationToken ct)
        {
            LastEmail = email;
            return Task.FromResult(Candidate);
        }

        public Task RegisterFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
        {
            FailedCalls++;
            return Task.CompletedTask;
        }

        public Task ResetFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
        {
            ResetCalls++;
            return Task.CompletedTask;
        }

        public Task<short> GetRoleIdAsync(string roleCode, CancellationToken ct) => throw new NotSupportedException();

        public Task<MeResponse?> FindMeAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> AddWithVerificationAsync(User user, EmailVerificationToken token, Func<Task> beforeCommit, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed record CreatedToken(
        Guid UserId, Guid FamilyId, string TokenHash, DateTimeOffset ExpiresAt, IPAddress? Ip, DateTimeOffset CreatedAt);

    private sealed class FakeRefreshTokens : IRefreshTokenStore
    {
        public List<CreatedToken> Created { get; } = [];

        public Task CreateAsync(
            Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, IPAddress? createdIp, DateTimeOffset now,
            CancellationToken ct)
        {
            Created.Add(new CreatedToken(userId, familyId, tokenHash, expiresAt, createdIp, now));
            return Task.CompletedTask;
        }

        public Task<RotateOutcome> RotateAsync(
            string tokenHash, DateTimeOffset now, string newTokenHash, DateTimeOffset newExpiresAt, IPAddress? createdIp,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public int VerifyCalls { get; private set; }
        public int DummyCalls { get; private set; }

        public string Hash(string password) => throw new NotSupportedException();

        public bool Verify(string password, string hash)
        {
            VerifyCalls++;
            return password == CorrectPassword;
        }

        public void VerifyAgainstDummy(string password) => DummyCalls++;
    }

    private sealed class FakeIssuer : IAccessTokenIssuer
    {
        public List<(Guid UserId, string RoleCode)> Calls { get; } = [];

        public AccessToken Issue(Guid userId, string roleCode)
        {
            Calls.Add((userId, roleCode));
            return new AccessToken("token-gia", 900);
        }
    }
}
