using System.Data.Common;
using SocialApp.Modules.Moderation.Application;
using SocialApp.Modules.Moderation.Application.Reports;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Moderation;

/// <summary>
/// GĐ6 D7b — phần thuần của hàng đợi và chi tiết: cursor, validator, ráp chi tiết (gom lịch sử theo lần quyết — L-D15, đối tượng
/// biến mất, ký URL sau tầng 2). Bản HTTP trên Postgres + Redis thật ở <c>ReportQueueTests</c>.
/// </summary>
public sealed class ReportReadServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cursor_ma_hoa_roi_giai_ma_ra_dung_gia_tri_va_khong_co_ky_tu_phai_escape()
    {
        var cursor = new ReportQueueCursor(T0.AddTicks(1234567), Guid.NewGuid());

        var raw = cursor.Encode();

        Assert.DoesNotContain(raw, c => c is '+' or '/' or '=');
        Assert.True(ReportQueueCursor.TryDecode(raw, out var decoded));
        Assert.Equal(cursor, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rac")]
    [InlineData("!!!")]
    [InlineData("MjAyNi0wOS0yNQ")]   // "2026-09-25" — thiếu phần id
    public void Cursor_rac_khong_nem_ma_tra_false(string raw) =>
        Assert.False(ReportQueueCursor.TryDecode(raw, out _));

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("open", null, 50, true)]
    [InlineData("closed", null, null, false)]
    [InlineData("OPEN", null, null, false)]
    [InlineData(null, "rac", null, false)]
    [InlineData(null, null, 0, false)]
    [InlineData(null, null, 51, false)]
    public void Validator_status_cursor_limit(string? status, string? cursor, int? limit, bool valid)
    {
        var result = new ListReportsQueryValidator().Validate(new ListReportsQuery { Status = status, Cursor = cursor, Limit = limit });
        Assert.Equal(valid, result.IsValid);
    }

    /// <summary>
    /// L-D15: báo cáo đóng cùng một lần quyết (cùng người, cùng mốc, cùng ghi chú) là MỘT dòng lịch sử; hai lần quyết là hai dòng,
    /// cũ nhất trước. Báo cáo mở không vào lịch sử.
    /// </summary>
    [Fact]
    public async Task Lich_su_gom_theo_lan_quyet_bao_cao_mo_tach_rieng()
    {
        var moderator = Guid.NewGuid();
        var target = Guid.NewGuid();
        var open = Row("open", created: T0.AddHours(3));
        var queries = new FakeQueries(new ReportThread("post", target,
        [
            Row("dismissed", moderator, T0.AddHours(1), "Không vi phạm."),
            Row("dismissed", moderator, T0.AddHours(1), "Không vi phạm."),
            Row("resolved", moderator, T0.AddHours(2), null),
            open,
        ]));
        var service = Service(queries, new FakeTargets());

        var detail = (await service.GetAsync(Guid.NewGuid(), default)).Value!;

        Assert.Equal(
            [
                new ReportHistoryEntry("dismissed", moderator, T0.AddHours(1), "Không vi phạm."),
                new ReportHistoryEntry("resolved", moderator, T0.AddHours(2), null),
            ],
            detail.History);
        Assert.Equal([open.ReportId], detail.OpenReports.Select(r => r.ReportId));
    }

    /// <summary>Đối tượng không còn trong bảng nào → <c>deleted</c>, không tác giả, không nội dung — không 404, không ném.</summary>
    [Fact]
    public async Task Doi_tuong_bien_mat_thi_status_deleted_moi_truong_noi_dung_null()
    {
        var target = Guid.NewGuid();
        var service = Service(new FakeQueries(new ReportThread("post", target, [Row("open")])), new FakeTargets());

        var snapshot = (await service.GetAsync(Guid.NewGuid(), default)).Value!.Target;

        Assert.Equal(("post", target, "deleted"), (snapshot.Type, snapshot.Id, snapshot.Status));
        Assert.Null(snapshot.Author);
        Assert.Null(snapshot.Body);
        Assert.Empty(snapshot.Media);
        Assert.Null(snapshot.PostId);
        Assert.Null(snapshot.CreatedAt);
        Assert.Null(snapshot.EditedAt);
    }

    /// <summary>Ảnh và avatar ký bằng <c>IObjectStorage</c>, theo thứ tự ảnh chụp; tác giả không có hồ sơ → <c>author: null</c>.</summary>
    [Fact]
    public async Task Anh_duoc_ky_theo_thu_tu_tac_gia_khong_ho_so_thi_null()
    {
        var target = Guid.NewGuid();
        var author = Guid.NewGuid();
        var targets = new FakeTargets();
        targets.Add(new TargetSnapshot(
            new ModerationTarget(ModerationTargetType.Post, target), "published", author, "Nội dung", ["k/1.jpg", "k/2.jpg"],
            target, T0, null));
        var service = Service(new FakeQueries(new ReportThread("post", target, [Row("open")])), targets);

        var snapshot = (await service.GetAsync(Guid.NewGuid(), default)).Value!.Target;

        Assert.Equal(["https://ky.invalid/k/1.jpg", "https://ky.invalid/k/2.jpg"], snapshot.Media.Select(m => m.Url));
        Assert.Null(snapshot.Author);
        Assert.Equal("Nội dung", snapshot.Body);
    }

    [Fact]
    public async Task Bao_cao_khong_ton_tai_404()
    {
        var result = await Service(new FakeQueries(null), new FakeTargets()).GetAsync(Guid.NewGuid(), default);
        Assert.Equal(ModerationErrors.ReportNotFound, result.Error);
    }

    /// <summary>Cursor dựng từ dòng CUỐI của trang trả về, không từ dòng thừa thứ <c>limit + 1</c>.</summary>
    [Fact]
    public async Task Cursor_trang_sau_neo_vao_dong_cuoi_trang_nay()
    {
        var rows = Enumerable.Range(0, 3)
            .Select(i => new ReportQueueRow(Guid.NewGuid(), "post", Guid.NewGuid(), 1, new Dictionary<string, int> { ["spam"] = 1 },
                T0.AddMinutes(i)))
            .ToList();
        var service = Service(new FakeQueries(null, rows), new FakeTargets());

        var page = await service.ListAsync(new ListReportsQuery { Limit = 2 }, default);

        Assert.Equal(2, page.Items.Count);
        Assert.True(ReportQueueCursor.TryDecode(page.NextCursor, out var next));
        Assert.Equal(new ReportQueueCursor(rows[1].FirstReportedAt, rows[1].TargetId), next);
    }

    private static ReportRow Row(
        string status, Guid? resolver = null, DateTimeOffset? resolvedAt = null, string? note = null, DateTimeOffset? created = null) =>
        new(Guid.NewGuid(), "spam", null, status, resolver, resolvedAt, note, created ?? T0);

    private static ReportReadService Service(IReportQueries queries, IModerationTargets targets) =>
        new(queries, targets, new EmptyDirectory(), new SigningOnlyStorage());

    private sealed class FakeQueries(ReportThread? thread, IReadOnlyList<ReportQueueRow>? rows = null) : IReportQueries
    {
        public Task<IReadOnlyList<ReportQueueRow>> ListOpenAsync(ReportQueueCursor? after, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReportQueueRow>>([.. (rows ?? []).Take(take)]);

        public Task<ReportThread?> FindThreadAsync(Guid reportId, CancellationToken ct) => Task.FromResult(thread);
    }

    private sealed class FakeTargets : IModerationTargets
    {
        private readonly Dictionary<ModerationTarget, TargetSnapshot> _items = [];

        public void Add(TargetSnapshot snapshot) => _items[snapshot.Target] = snapshot;

        public bool Supports(ModerationTargetType type) => true;

        public Task<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>> GetSnapshotsAsync(
            IReadOnlyCollection<ModerationTarget> targets, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>>(
                targets.Where(_items.ContainsKey).ToDictionary(t => t, t => _items[t]));

        public Task<bool> CanViewAsync(Guid actorId, ModerationTarget target, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<HideOutcome> HideAsync(DbTransaction tx, ModerationTarget target, string reasonCode, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, ModerationTarget target, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyDirectory : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserCard>>(new Dictionary<Guid, UserCard>());
    }

    /// <summary>Chỉ ký URL — khuôn <c>PostResponseMapperTests</c>.</summary>
    private sealed class SigningOnlyStorage : IObjectStorage
    {
        public string CreatePresignedPut(string key, string contentType, long contentLength) => throw new NotSupportedException();

        public string CreatePresignedGet(string key) => $"https://ky.invalid/{key}";

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(string prefix, string? token, int maxKeys, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
