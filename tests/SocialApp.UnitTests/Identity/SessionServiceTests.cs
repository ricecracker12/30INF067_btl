using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.Modules.Identity.Application.Session;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Results;
using Xunit;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// Ánh xạ kết quả xoay vòng → HTTP ở tầng service, store giả. Thuật toán khóa dòng thật (FOR UPDATE, ân hạn, reuse) nằm ở
/// store và được kiểm trên Postgres thật trong RefreshTests.
/// </summary>
public sealed class SessionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly IPAddress Ip = IPAddress.Parse("203.0.113.9");
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeRefreshTokens _store = new();
    private readonly FakeIssuer _issuer = new();

    private SessionService Service() => new(
        _store, _issuer, Options.Create(new JwtOptions { RefreshTokenDays = 7 }), new FixedTime(Now),
        NullLogger<SessionService>.Instance);

    private Task<Result<RefreshSuccess>> RefreshAsync(string? cookie = "cookie-cu") =>
        Service().RefreshAsync(cookie, Ip, CancellationToken.None);

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public async Task Khong_co_cookie_401_va_khong_cham_store()
    {
        var result = await RefreshAsync(cookie: null);

        Assert.Equal(IdentityErrors.SessionInvalid, result.Error);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public async Task Rotated_bam_token_cu_va_moi_han_7_ngay_phat_access_theo_role_doc_tu_store()
    {
        _store.Outcome = new RotateOutcome.Rotated(UserId, "MODERATOR");

        var result = await RefreshAsync();

        Assert.True(result.IsSuccess);
        var call = Assert.Single(_store.Calls);
        Assert.Equal(Sha256Hex("cookie-cu"), call.TokenHash);
        Assert.Equal(Sha256Hex(result.Value!.RefreshPlain), call.NewTokenHash);
        Assert.Equal(Now, call.Now);
        Assert.Equal(Now.AddDays(7), call.NewExpiresAt);
        Assert.Equal(Ip, call.Ip);
        Assert.Equal((UserId, "MODERATOR"), Assert.Single(_issuer.Calls));
        Assert.Equal("token-gia", result.Value.Access.Token);
    }

    [Fact]
    public async Task Grace_cung_phat_cap_token_moi()
    {
        _store.Outcome = new RotateOutcome.Grace(UserId, "USER");

        var result = await RefreshAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal((UserId, "USER"), Assert.Single(_issuer.Calls));
    }

    [Fact]
    public async Task ReuseDetected_401_CUNG_loi_va_khong_phat_token()
    {
        _store.Outcome = new RotateOutcome.ReuseDetected(UserId);

        var result = await RefreshAsync();

        Assert.Equal(IdentityErrors.SessionInvalid, result.Error);
        Assert.Empty(_issuer.Calls);
    }

    [Fact]
    public async Task Invalid_401_CUNG_loi_va_khong_phat_token()
    {
        _store.Outcome = RotateOutcome.Invalid.Instance;

        var result = await RefreshAsync();

        Assert.Equal(IdentityErrors.SessionInvalid, result.Error);
        Assert.Empty(_issuer.Calls);
    }

    [Fact]
    public async Task Logout_khong_cookie_khong_cham_store()
    {
        await Service().LogoutAsync(refreshCookie: null, UserId, CancellationToken.None);

        Assert.Empty(_store.Revocations);
    }

    /// <summary>Store nhận băm của cookie và actor do controller truyền (từ access token) — kiểm chủ sở hữu nằm ở store.</summary>
    [Fact]
    public async Task Logout_bam_cookie_va_truyen_actor_cho_store()
    {
        await Service().LogoutAsync("cookie-cu", UserId, CancellationToken.None);

        Assert.Equal((Sha256Hex("cookie-cu"), UserId, Now), Assert.Single(_store.Revocations));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record RotateCall(string TokenHash, DateTimeOffset Now, string NewTokenHash, DateTimeOffset NewExpiresAt, IPAddress? Ip);

    private sealed class FakeRefreshTokens : IRefreshTokenStore
    {
        public RotateOutcome Outcome { get; set; } = RotateOutcome.Invalid.Instance;
        public List<RotateCall> Calls { get; } = [];

        public Task<RotateOutcome> RotateAsync(
            string tokenHash, DateTimeOffset now, string newTokenHash, DateTimeOffset newExpiresAt, IPAddress? createdIp,
            CancellationToken ct)
        {
            Calls.Add(new RotateCall(tokenHash, now, newTokenHash, newExpiresAt, createdIp));
            return Task.FromResult(Outcome);
        }

        public Task CreateAsync(
            Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, IPAddress? createdIp, DateTimeOffset now,
            CancellationToken ct) => throw new NotSupportedException();

        public List<(string TokenHash, Guid OwnerUserId, DateTimeOffset Now)> Revocations { get; } = [];

        public Task<int> RevokeFamilyAsync(string tokenHash, Guid ownerUserId, DateTimeOffset now, CancellationToken ct)
        {
            Revocations.Add((tokenHash, ownerUserId, now));
            return Task.FromResult(2);
        }
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
