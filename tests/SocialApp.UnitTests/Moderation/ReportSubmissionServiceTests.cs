using System.Data.Common;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.UnitTests.Moderation;

/// <summary>
/// GĐ6 D6 — thứ tự kiểm của <see cref="ReportSubmissionService"/> (Đ-6.12): hỗ trợ → tồn tại → THẤY ĐƯỢC → của mình → ghi. Bốn
/// nhánh đầu trả CÙNG một lỗi; "của mình" đứng sau "thấy được". Bản HTTP đầy đủ ở <c>ReportSubmissionTests</c>.
/// </summary>
public sealed class ReportSubmissionServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static CreateReportRequest Request(Guid targetId, string targetType = "post", string? detail = null) =>
        new() { TargetType = targetType, TargetId = targetId, ReasonCode = "spam", Detail = detail };

    private static (ReportSubmissionService Service, FakeTargets Targets, FakeStore Store) Build()
    {
        var targets = new FakeTargets();
        var store = new FakeStore();
        return (new ReportSubmissionService(targets, store, TimeProvider.System), targets, store);
    }

    [Fact]
    public async Task Loai_chua_ho_tro_404_khong_doc_gi()
    {
        var (service, targets, store) = Build();
        targets.Supported = false;

        var result = await service.SubmitAsync(Actor, Request(Guid.NewGuid(), "comment"), default);

        Assert.Equal(ModerationErrors.ReportTargetNotFound, result.Error);
        Assert.Equal(0, targets.SnapshotCalls);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Khong_ton_tai_404()
    {
        var (service, _, store) = Build();

        var result = await service.SubmitAsync(Actor, Request(Guid.NewGuid()), default);

        Assert.Equal(ModerationErrors.ReportTargetNotFound, result.Error);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Ton_tai_ma_khong_thay_duoc_cung_loi_404_voi_khong_ton_tai()
    {
        var (service, targets, store) = Build();
        var id = Guid.NewGuid();
        targets.Add(id, author: Other, canView: false);

        var result = await service.SubmitAsync(Actor, Request(id), default);

        Assert.Equal(ModerationErrors.ReportTargetNotFound, result.Error);
        Assert.Equal(0, store.Calls);
    }

    /// <summary>
    /// "Của mình" SAU "thấy được": đối tượng mà người gọi không thấy thì luôn 404, kể cả khi ảnh chụp nói tác giả là người gọi —
    /// đảo thứ tự thì 400 lộ ra trước. (Thực tế tác giả luôn thấy bài mình; ca này khóa THỨ TỰ, không khóa dữ liệu.)
    /// </summary>
    [Fact]
    public async Task Khong_thay_duoc_thi_404_truoc_khi_so_tac_gia()
    {
        var (service, targets, _) = Build();
        var id = Guid.NewGuid();
        targets.Add(id, author: Actor, canView: false);

        var result = await service.SubmitAsync(Actor, Request(id), default);

        Assert.Equal(ModerationErrors.ReportTargetNotFound, result.Error);
    }

    [Fact]
    public async Task Cua_chinh_minh_400_khong_ghi()
    {
        var (service, targets, store) = Build();
        var id = Guid.NewGuid();
        targets.Add(id, author: Actor, canView: true);

        var result = await service.SubmitAsync(Actor, Request(id), default);

        Assert.Equal("targetId", Assert.Single(result.Error!.Value.Errors!).Key);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Thay_duoc_cua_nguoi_khac_ghi_voi_detail_da_chuan_hoa()
    {
        var (service, targets, store) = Build();
        var id = Guid.NewGuid();
        targets.Add(id, author: Other, canView: true);

        var result = await service.SubmitAsync(Actor, Request(id, detail: "  Lừa đảo.  "), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.Calls);
        Assert.Equal((Actor, "post", id, "spam", "Lừa đảo."), store.Last);
    }

    private sealed class FakeTargets : IModerationTargets
    {
        private readonly Dictionary<ModerationTarget, (TargetSnapshot Snapshot, bool CanView)> _items = [];

        public bool Supported { get; set; } = true;

        public int SnapshotCalls { get; private set; }

        public void Add(Guid id, Guid author, bool canView)
        {
            var target = new ModerationTarget(ModerationTargetType.Post, id);
            _items[target] = (new TargetSnapshot(target, "published", author, "x", [], id, DateTimeOffset.UtcNow, null), canView);
        }

        public bool Supports(ModerationTargetType type) => Supported;

        public Task<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>> GetSnapshotsAsync(
            IReadOnlyCollection<ModerationTarget> targets, CancellationToken ct = default)
        {
            SnapshotCalls++;
            return Task.FromResult<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>>(
                targets.Where(_items.ContainsKey).ToDictionary(t => t, t => _items[t].Snapshot));
        }

        public Task<bool> CanViewAsync(Guid actorId, ModerationTarget target, CancellationToken ct = default) =>
            Task.FromResult(_items.TryGetValue(target, out var item) && item.CanView);

        public Task<HideOutcome> HideAsync(DbTransaction tx, ModerationTarget target, string reasonCode, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, ModerationTarget target, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStore : IReportStore
    {
        public int Calls { get; private set; }

        public (Guid Reporter, string Type, Guid Target, string Reason, string? Detail) Last { get; private set; }

        public Task<(ReportReceipt Receipt, bool Created)> CreateOrGetOpenAsync(
            Guid reporterId, string targetType, Guid targetId, string reasonCode, string? detail, DateTimeOffset now,
            CancellationToken ct)
        {
            Calls++;
            Last = (reporterId, targetType, targetId, reasonCode, detail);
            return Task.FromResult((new ReportReceipt(Guid.NewGuid(), "open", now), true));
        }
    }
}
