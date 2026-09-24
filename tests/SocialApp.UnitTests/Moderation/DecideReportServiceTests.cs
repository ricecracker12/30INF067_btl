using System.Data.Common;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.UnitTests.Moderation;

/// <summary>
/// GĐ6 D7c — phần thuần của quyết định: bảng <c>decision × targetType</c> (chín ô, Đ-6.13), validator, và thứ tự của
/// <see cref="DecideReportService"/> — tầng 2 kép TRƯỚC mọi I/O (L-D12), event CHỈ khi bài vừa bị ẩn. Transaction thật ở
/// <c>DecideReportTests</c> / <c>ModerationTransactionTests</c>.
/// </summary>
public sealed class DecideReportServiceTests
{
    [Theory]
    [InlineData("hide", ModerationTargetType.Post, true)]
    [InlineData("hide", ModerationTargetType.Comment, true)]
    [InlineData("hide", ModerationTargetType.User, false)]
    [InlineData("dismiss", ModerationTargetType.Post, true)]
    [InlineData("dismiss", ModerationTargetType.Comment, true)]
    [InlineData("dismiss", ModerationTargetType.User, true)]
    [InlineData("resolve", ModerationTargetType.Post, false)]
    [InlineData("resolve", ModerationTargetType.Comment, false)]
    [InlineData("resolve", ModerationTargetType.User, true)]
    public void Bang_decision_x_targetType_chin_o(string decision, ModerationTargetType type, bool allowed) =>
        Assert.Equal(allowed, DecisionRules.IsAllowed(decision, type));

    [Theory]
    [InlineData("hide", null, null, true)]
    [InlineData("resolve", null, "Đã khóa tài khoản.", true)]
    [InlineData("resolve", null, null, false)]
    [InlineData("resolve", null, "   ", false)]
    [InlineData("HIDE", null, null, false)]
    [InlineData(null, null, null, false)]
    [InlineData("dismiss", "abuse", null, false)]
    [InlineData("dismiss", "violence", null, true)]
    public void Validator_decision_reasonCode_note(string? decision, string? reasonCode, string? note, bool valid) =>
        Assert.Equal(valid, new DecideReportRequestValidator()
            .Validate(new DecideReportRequest { Decision = decision, ReasonCode = reasonCode, Note = note }).IsValid);

    [Fact]
    public void Validator_note_501_ky_tu_sau_trim_la_sai() =>
        Assert.False(new DecideReportRequestValidator()
            .Validate(new DecideReportRequest { Decision = "dismiss", Note = "  " + new string('a', 501) + "  " }).IsValid);

    /// <summary>L-D12: vai trò thiếu <c>post.hide</c> nhận 403 TRƯỚC mọi I/O — không đọc báo cáo, một dòng audit <c>tx: null</c>.</summary>
    [Fact]
    public async Task Hide_thieu_post_hide_403_truoc_moi_IO_va_ghi_access_denied()
    {
        var (service, store, _, audit, events) = Build(permissions: ["report.resolve"]);
        var reportId = Guid.NewGuid();

        var result = await service.DecideAsync(reportId, Guid.NewGuid(), "REVIEWER", new DecideReportRequest { Decision = "hide" }, default);

        Assert.Equal(403, result.Error!.Value.Status);
        Assert.Equal(0, store.FindCalls);
        var (tx, entry) = Assert.Single(audit.Entries);
        Assert.Null(tx);
        Assert.Equal((AuditActions.AccessDenied, "report", (Guid?)reportId), (entry.Action, entry.TargetType, entry.TargetId));
        Assert.Equal("post.hide", entry.Metadata!["permission"]);
        Assert.Empty(events.Published);
    }

    /// <summary>Cùng vai trò đó <c>dismiss</c> được — tầng 2 kép chỉ áp cho <c>hide</c>.</summary>
    [Fact]
    public async Task Dismiss_khong_can_post_hide()
    {
        var (service, store, _, audit, _) = Build(permissions: ["report.resolve"]);

        var result = await service.DecideAsync(Guid.NewGuid(), Guid.NewGuid(), "REVIEWER", new DecideReportRequest { Decision = "dismiss" }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.DecideCalls);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Sai_cap_decision_targetType_400_khong_mo_transaction()
    {
        var (service, store, _, _, _) = Build(permissions: ["report.resolve", "post.hide"], targetType: "user");

        var result = await service.DecideAsync(Guid.NewGuid(), Guid.NewGuid(), "MODERATOR", new DecideReportRequest { Decision = "hide" }, default);

        // InvalidDecision là property dựng Error mới mỗi lần (dictionary errors mới) — so theo status + key, không theo tham chiếu.
        Assert.Equal(400, result.Error!.Value.Status);
        Assert.True(result.Error.Value.Errors!.ContainsKey("decision"));
        Assert.Equal(0, store.DecideCalls);
    }

    /// <summary>Event SAU store (đã COMMIT), đủ vai: bài, bài cha, TÁC GIẢ (không phải Moderator), lý do của REQUEST (cạm bẫy 5).</summary>
    [Fact]
    public async Task Hide_vua_an_phat_ContentHidden_dung_vai_ly_do_cua_request()
    {
        var (service, store, targets, _, events) = Build(permissions: ["report.resolve", "post.hide"]);
        var moderator = Guid.NewGuid();

        var result = await service.DecideAsync(
            Guid.NewGuid(), moderator, "MODERATOR", new DecideReportRequest { Decision = "hide", ReasonCode = "violence" }, default);

        Assert.Equal("hidden", result.Value!.TargetStatus);
        var hidden = Assert.IsType<ContentHidden>(Assert.Single(events.Published));
        Assert.Equal(new ContentHidden(ModerationTargetType.Post, targets.TargetId, targets.TargetId, targets.AuthorId, "violence"), hidden);
        Assert.Equal("violence", store.LastCommand!.ReasonCode);
    }

    [Fact]
    public async Task Bai_da_an_tu_truoc_khong_phat_lai()
    {
        var (service, store, _, _, events) = Build(permissions: ["report.resolve", "post.hide"]);
        store.Next = new DecisionOutcome(DecisionStatus.Decided, [Guid.NewGuid()], HideOutcome.AlreadyHidden);

        var result = await service.DecideAsync(Guid.NewGuid(), Guid.NewGuid(), "MODERATOR", new DecideReportRequest { Decision = "hide" }, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task Da_quyet_roi_409_khong_phat_event()
    {
        var (service, store, _, _, events) = Build(permissions: ["report.resolve", "post.hide"]);
        store.Next = DecisionOutcome.Stopped(DecisionStatus.AlreadyDecided);

        var result = await service.DecideAsync(Guid.NewGuid(), Guid.NewGuid(), "MODERATOR", new DecideReportRequest { Decision = "hide" }, default);

        Assert.Equal(ModerationErrors.ReportAlreadyDecided, result.Error);
        Assert.Empty(events.Published);
    }

    private static (DecideReportService, FakeStore, FakeTargets, FakeAudit, FakeEvents) Build(
        string[] permissions, string targetType = "post")
    {
        var targets = new FakeTargets();
        var store = new FakeStore(new ReportHead(targetType, targets.TargetId, "spam"));
        var audit = new FakeAudit();
        var events = new FakeEvents();
        var service = new DecideReportService(store, targets, new FakeCache(permissions), audit, events, TimeProvider.System);
        return (service, store, targets, audit, events);
    }

    private sealed class FakeStore(ReportHead head) : IModerationDecisionStore
    {
        public int FindCalls { get; private set; }

        public int DecideCalls { get; private set; }

        public ReportDecisionCommand? LastCommand { get; private set; }

        public DecisionOutcome? Next { get; set; }

        public Task<ReportHead?> FindReportAsync(Guid reportId, CancellationToken ct)
        {
            FindCalls++;
            return Task.FromResult<ReportHead?>(head);
        }

        public Task<DecisionOutcome> DecideAsync(ReportDecisionCommand command, CancellationToken ct)
        {
            DecideCalls++;
            LastCommand = command;
            return Task.FromResult(Next ?? new DecisionOutcome(
                DecisionStatus.Decided, [command.ReportId], command.Decision == "hide" ? HideOutcome.Hidden : null));
        }

        public Task<RestoreOutcome> RestoreAsync(ModerationTarget target, string targetType, string? note, Guid actorId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTargets : IModerationTargets
    {
        public Guid TargetId { get; } = Guid.NewGuid();

        public Guid AuthorId { get; } = Guid.NewGuid();

        public bool Supports(ModerationTargetType type) => true;

        public Task<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>> GetSnapshotsAsync(
            IReadOnlyCollection<ModerationTarget> targets, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>>(targets.ToDictionary(
                t => t,
                t => new TargetSnapshot(t, "published", AuthorId, "x", [], t.Type == ModerationTargetType.User ? null : t.Id,
                    DateTimeOffset.UtcNow, null)));

        public Task<bool> CanViewAsync(Guid actorId, ModerationTarget target, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<HideOutcome> HideAsync(DbTransaction tx, ModerationTarget target, string reasonCode, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, ModerationTarget target, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCache(string[] permissions) : IPermissionCache
    {
        public ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string>(permissions));

        public void Invalidate(string roleCode)
        {
        }

        public void InvalidateAll()
        {
        }
    }

    private sealed class FakeAudit : IAuditTrail
    {
        public List<(DbTransaction? Tx, AuditEntry Entry)> Entries { get; } = [];

        public Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add((tx, entry));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEvents : IEventPublisher
    {
        public List<IIntegrationEvent> Published { get; } = [];

        public void Publish(IIntegrationEvent integrationEvent) => Published.Add(integrationEvent);
    }
}
