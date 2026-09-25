using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.Identity.Application.Admin.Users;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.UnitTests.Identity;

/// <summary>
/// GĐ6 D3, D4: thứ tự DB → Redis (Đ-6.6, B.10 #1), thử lại 3 lần của <see cref="UserRevoker"/>, và tầng 2 kép của đổi vai trò
/// (L-D18). Integration (<c>AccountLockTests</c>, <c>AssignRoleTests</c>) chứng minh hành vi trên Postgres + Redis thật; ở đây khẳng
/// định những thứ integration không nhìn thấy: store được gọi TRƯỚC kho thu hồi, nhánh lỗi KHÔNG chạm kho thu hồi, lần từ chối của
/// tầng 2 kép xảy ra TRƯỚC khi mở transaction, và thời gian chờ giữa các lần thử.
/// </summary>
public sealed class AccountAdministrationServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    /// <summary>"HR" chỉ có role.assign (ví dụ "Nhân sự" của Đ-6.9); "HR_MANAGER" có thêm role.manage.</summary>
    private static readonly Dictionary<string, string[]> Grants = new()
    {
        ["HR"] = ["role.assign"],
        ["HR_MANAGER"] = ["role.assign", "role.manage"],
    };

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

        public Task<AdminOutcome> AssignRoleAsync(
            Guid targetId, Guid actorId, string roleCode, bool actorCanManageRoles, DateTimeOffset now, CancellationToken ct)
        {
            journal.Calls.Add($"store.assign:{roleCode}:{(actorCanManageRoles ? "manage" : "assign-only")}");
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

    /// <summary>Nguồn quyền tĩnh — <c>IsAllowedAsync</c> (thật) làm short-circuit ADMIN trước khi hỏi tới đây.</summary>
    private sealed class FakePermissions(Dictionary<string, string[]> grants) : IPermissionCache
    {
        public ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(grants.GetValueOrDefault(roleCode, []), StringComparer.Ordinal));

        public void Invalidate(string roleCode) { }

        public void InvalidateAll() { }
    }

    private sealed class FakeAudit(Journal journal) : IAuditTrail
    {
        public List<(DbTransaction? Tx, AuditEntry Entry)> Entries { get; } = [];

        public Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct = default)
        {
            journal.Calls.Add($"audit:{entry.Action}");
            Entries.Add((tx, entry));
            return Task.CompletedTask;
        }
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
        var (service, journal, _) = BuildWithAudit(outcome, revokeFailures, time);
        return (service, journal);
    }

    private static (AccountAdministrationService Service, Journal Journal, FakeAudit Audit) BuildWithAudit(
        AdminOutcome outcome, int revokeFailures = 0, TimeProvider? time = null)
    {
        var journal = new Journal();
        time ??= new InstantTime();
        var revoker = new UserRevoker(new FakeRevocation(journal, revokeFailures), time, NullLogger<UserRevoker>.Instance);
        var reads = new AdminUserReadService(new FakeQueries(), new EmptyDirectory(), time);
        var audit = new FakeAudit(journal);
        var service = new AccountAdministrationService(
            new FakeStore(journal, outcome), revoker, reads, new FakePermissions(Grants), audit, time);
        return (service, journal, audit);
    }

    // --- D3: khóa / mở khóa ---------------------------------------------------------------------------------------------------

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

    // --- D4: đổi vai trò ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Doi_vai_tro_ghi_DB_truoc_Redis_sau()
    {
        var (service, journal) = Build(AdminOutcome.Changed);

        var result = await service.AssignRoleAsync(Target, Actor, "HR", new AssignRoleRequest { RoleCode = "MODERATOR" }, default);

        Assert.Equal(RevocationStates.Applied, result.Value!.Revocation);
        Assert.Equal(["store.assign:MODERATOR:assign-only", "redis.revoke"], journal.Calls);
    }

    [Theory]
    [InlineData(AdminOutcome.NoChange, null)]
    [InlineData(AdminOutcome.LastAdmin, 409)]
    [InlineData(AdminOutcome.UnknownRole, 400)]
    [InlineData(AdminOutcome.NotFound, 404)]
    public async Task Doi_vai_tro_nhanh_khong_Changed_khong_cham_Redis(AdminOutcome outcome, int? status)
    {
        var (service, journal) = Build(outcome);

        var result = await service.AssignRoleAsync(Target, Actor, "ADMIN", new AssignRoleRequest { RoleCode = "USER" }, default);

        Assert.DoesNotContain("redis.revoke", journal.Calls);
        if (status is null)
            Assert.Equal(RevocationStates.NotNeeded, result.Value!.Revocation);
        else
            Assert.Equal(status, result.Error!.Value.Status);
    }

    [Fact]
    public async Task Vai_tro_dich_khong_ton_tai_400_errors_roleCode()
    {
        var (service, _) = Build(AdminOutcome.UnknownRole);

        var result = await service.AssignRoleAsync(Target, Actor, "ADMIN", new AssignRoleRequest { RoleCode = "user" }, default);

        Assert.Equal(["roleCode"], result.Error!.Value.Errors!.Keys);
    }

    /// <summary>L-D18 vế 1: nâng LÊN ADMIN mà thiếu role.manage → 403 TRƯỚC mọi I/O của store, một dòng access.denied tx null.</summary>
    [Fact]
    public async Task Nang_len_ADMIN_khong_co_role_manage_403_khong_mo_transaction_co_audit()
    {
        var (service, journal, audit) = BuildWithAudit(AdminOutcome.Changed);

        var result = await service.AssignRoleAsync(Target, Actor, "HR", new AssignRoleRequest { RoleCode = "ADMIN" }, default);

        Assert.Equal(403, result.Error!.Value.Status);
        Assert.Equal(["audit:access.denied"], journal.Calls);
        var (tx, entry) = Assert.Single(audit.Entries);
        Assert.Null(tx);
        Assert.Equal((Actor, "user", (Guid?)Target), (entry.ActorId, entry.TargetType, entry.TargetId));
        Assert.Equal("role.manage", entry.Metadata!["permission"]);
    }

    /// <summary>L-D18 vế 2: người bị đổi ĐANG là ADMIN — store báo Forbidden sau khi khóa dòng → 403 + audit, không Redis.</summary>
    [Fact]
    public async Task Ha_ADMIN_khong_co_role_manage_store_Forbidden_403_co_audit()
    {
        var (service, journal) = Build(AdminOutcome.Forbidden);

        var result = await service.AssignRoleAsync(Target, Actor, "HR", new AssignRoleRequest { RoleCode = "USER" }, default);

        Assert.Equal(403, result.Error!.Value.Status);
        Assert.Equal(["store.assign:USER:assign-only", "audit:access.denied"], journal.Calls);
    }

    [Theory]
    [InlineData("HR_MANAGER")]   // vai trò tự tạo có role.manage
    [InlineData("ADMIN")]        // short-circuit của IsAllowedAsync — không cần dòng role_permissions
    public async Task Co_role_manage_thi_cham_duoc_ADMIN(string actorRole)
    {
        var (service, journal) = Build(AdminOutcome.Changed);

        var result = await service.AssignRoleAsync(Target, Actor, actorRole, new AssignRoleRequest { RoleCode = "ADMIN" }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(["store.assign:ADMIN:manage", "redis.revoke"], journal.Calls);
    }

    [Fact]
    public async Task Claim_role_null_khong_co_role_manage()
    {
        var (service, journal) = Build(AdminOutcome.Changed);

        var result = await service.AssignRoleAsync(Target, Actor, null, new AssignRoleRequest { RoleCode = "ADMIN" }, default);

        Assert.Equal(403, result.Error!.Value.Status);
        Assert.DoesNotContain(journal.Calls, c => c.StartsWith("store.", StringComparison.Ordinal));
    }
}
