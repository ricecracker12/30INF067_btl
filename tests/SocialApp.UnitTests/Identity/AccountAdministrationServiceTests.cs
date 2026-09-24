using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.Identity.Application.Admin.Users;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// GĐ6 D3: thứ tự DB → Redis (Đ-6.6, B.10 #1) và thử lại 3 lần của <see cref="UserRevoker"/>. Integration (<c>AccountLockTests</c>)
/// chứng minh hành vi trên Postgres + Redis thật; ở đây khẳng định những thứ integration không nhìn thấy: store được gọi TRƯỚC kho
/// thu hồi, nhánh 409/400/NoChange KHÔNG chạm kho thu hồi, và thời gian chờ giữa các lần thử.
/// </summary>
public sealed class AccountAdministrationServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    private sealed class Journal
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class FakeStore(Journal journal, AdminOutcome outcome) : IAccountAdministrationStore
    {
        public Task<AdminOutcome> LockAsync(Guid targetId, Guid actorId, string reason, DateTimeOffset now, CancellationToken ct)
        {
            journal.Calls.Add($"store.lock:{reason}");
            return Task.FromResult(outcome);
        }

        public Task<AdminOutcome> UnlockAsync(Guid targetId, Guid actorId, DateTimeOffset now, CancellationToken ct)
        {
            journal.Calls.Add("store.unlock");
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeRevocation(Journal journal, int failures) : ITokenRevocationStore
    {
        private int _failuresLeft = failures;

        public Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default)
        {
            journal.Calls.Add("redis.revoke");
            if (_failuresLeft-- > 0)
                throw new InvalidOperationException("Redis giả hỏng");
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default) => Task.FromResult(false);

        public Task<RevocationCheck> CheckAsync(string userId, long issuedAtUnix, CancellationToken ct = default) =>
            Task.FromResult(RevocationCheck.NotRevoked);
    }

    private sealed class FakeQueries : IAdminUserQueries
    {
        public Task<IReadOnlyList<AdminUserRow>> ListAsync(
            AdminUserFilter filter, AdminUserCursor? after, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdminUserRow>>([]);

        public Task<AdminUserRow?> FindAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<AdminUserRow?>(new AdminUserRow(
                userId, "x@test.local", "USER", "Người dùng", "disabled", DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch));
    }

    private sealed class EmptyDirectory : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserCard>>(new Dictionary<Guid, UserCard>());
    }

    /// <summary>Timer bắn NGAY (không ngủ thật) và ghi lại thời gian chờ đã xin.</summary>
    private sealed class InstantTime : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            return base.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    private sealed class ListLogger : ILogger<UserRevoker>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (AccountAdministrationService Service, Journal Journal) Build(
        AdminOutcome outcome, int revokeFailures = 0, TimeProvider? time = null)
    {
        var journal = new Journal();
        time ??= new InstantTime();
        var revoker = new UserRevoker(new FakeRevocation(journal, revokeFailures), time, NullLogger<UserRevoker>.Instance);
        var reads = new AdminUserReadService(new FakeQueries(), new EmptyDirectory(), time);
        return (new AccountAdministrationService(new FakeStore(journal, outcome), revoker, reads, time), journal);
    }

    [Fact]
    public async Task Khoa_thanh_cong_ghi_DB_truoc_Redis_sau_va_reason_da_cat()
    {
        var (service, journal) = Build(AdminOutcome.Changed);

        var result = await service.LockAsync(Target, Actor, new LockRequest { Reason = "  spam  " }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(RevocationStates.Applied, result.Value!.Revocation);
        Assert.Equal(["store.lock:spam", "redis.revoke"], journal.Calls);
    }

    [Theory]
    [InlineData(AdminOutcome.LastAdmin, 409)]
    [InlineData(AdminOutcome.NotFound, 404)]
    public async Task Nhanh_loi_cua_store_khong_cham_Redis(AdminOutcome outcome, int status)
    {
        var (service, journal) = Build(outcome);

        var result = await service.LockAsync(Target, Actor, new LockRequest { Reason = "x" }, default);

        Assert.Equal(status, result.Error!.Value.Status);
        Assert.Equal(["store.lock:x"], journal.Calls);
    }

    [Fact]
    public async Task Tu_khoa_400_truoc_moi_IO()
    {
        var (service, journal) = Build(AdminOutcome.Changed);

        var result = await service.LockAsync(Actor, Actor, new LockRequest { Reason = "x" }, default);

        Assert.Equal(400, result.Error!.Value.Status);
        Assert.Contains("userId", result.Error!.Value.Errors!.Keys);
        Assert.Empty(journal.Calls);
    }

    [Fact]
    public async Task Khoa_tai_khoan_da_khoa_not_needed_khong_cham_Redis()
    {
        var (service, journal) = Build(AdminOutcome.NoChange);

        var result = await service.LockAsync(Target, Actor, new LockRequest { Reason = "x" }, default);

        Assert.Equal(RevocationStates.NotNeeded, result.Value!.Revocation);
        Assert.Equal(["store.lock:x"], journal.Calls);
    }

    [Theory]
    [InlineData(AdminOutcome.Changed)]
    [InlineData(AdminOutcome.NoChange)]
    public async Task Mo_khoa_khong_bao_gio_thu_hoi(AdminOutcome outcome)
    {
        var (service, journal) = Build(outcome);

        var result = await service.UnlockAsync(Target, Actor, default);

        Assert.Equal(RevocationStates.NotNeeded, result.Value!.Revocation);
        Assert.Equal(["store.unlock"], journal.Calls);
    }

    [Fact]
    public async Task Redis_hong_hai_lan_lan_ba_duoc_applied_cho_250_roi_500_ms()
    {
        var time = new InstantTime();
        var (service, journal) = Build(AdminOutcome.Changed, revokeFailures: 2, time: time);

        var result = await service.LockAsync(Target, Actor, new LockRequest { Reason = "x" }, default);

        Assert.Equal(RevocationStates.Applied, result.Value!.Revocation);
        Assert.Equal(3, journal.Calls.Count(c => c == "redis.revoke"));
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)], time.Delays);
    }

    [Fact]
    public async Task Redis_hong_ca_ba_lan_deferred_log_Error_mot_lan()
    {
        var journal = new Journal();
        var logger = new ListLogger();
        var revoker = new UserRevoker(new FakeRevocation(journal, failures: int.MaxValue), new InstantTime(), logger);

        var state = await revoker.RevokeAsync(Target);

        Assert.Equal(RevocationStates.Deferred, state);
        Assert.Equal(3, journal.Calls.Count);
        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains(Target.ToString(), message);
    }
}
