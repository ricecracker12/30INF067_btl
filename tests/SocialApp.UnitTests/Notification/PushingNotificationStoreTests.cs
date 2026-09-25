using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.Notification.Application;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;
using Xunit;

namespace SocialApp.UnitTests.Notification;

/// <summary>
/// C6 — <see cref="PushingNotificationStore"/>: đẩy SAU khi upsert trả về (đã COMMIT), đúng người nhận, đúng nhóm, kèm số chưa đọc tuyệt đối;
/// tự báo mình (<c>Skipped</c>) không đẩy; đẩy hỏng KHÔNG làm hỏng thông báo đã lưu (Đ-6.18 — lượt hỏi lại 30 giây vẫn thấy); upsert hỏng
/// thì vẫn ném (bus ghi log). Đường thật qua hub ở <c>NotificationHubTests</c>.
/// </summary>
public sealed class PushingNotificationStoreTests
{
    private static readonly Guid An = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Binh = Guid.Parse("01900000-0000-7000-8000-0000000000b2");

    private static readonly NotificationUpsert LoiMoi =
        new(An, "friend_request", $"friend_request:{Binh:D}", "user", Binh, null, Binh, null);

    private static PushingNotificationStore Store(FakeInner inner, FakePusher pusher) => new(
        inner,
        new NotificationService(new FakeQueries(), new FakeDirectory(), new FakeStorage()),
        pusher,
        NullLogger<PushingNotificationStore>.Instance);

    [Fact]
    public async Task Upsert_xong_moi_day_dung_nguoi_nhan_kem_so_chua_doc()
    {
        var inner = new FakeInner(UpsertResult.Created);
        var pusher = new FakePusher();

        Assert.Equal(UpsertResult.Created, await Store(inner, pusher).UpsertAsync(LoiMoi, default));

        var (recipient, pushed) = Assert.Single(pusher.Pushed);
        Assert.Equal(An, recipient);
        Assert.Equal(("friend_request", "Bình", 7), (pushed.Notification.Type, pushed.Notification.Actor!.DisplayName, pushed.UnreadTotal));
        Assert.True(inner.Done, "Đẩy trước khi upsert xong — người nhận có thể thấy thông báo 'ma' của một lần rollback.");
    }

    [Fact]
    public async Task Tu_bao_minh_khong_day()
    {
        var pusher = new FakePusher();
        Assert.Equal(UpsertResult.Skipped, await Store(new FakeInner(UpsertResult.Skipped), pusher).UpsertAsync(LoiMoi, default));
        Assert.Empty(pusher.Pushed);
    }

    [Fact]
    public async Task Day_hong_van_tra_ket_qua_upsert_khong_nem()
    {
        var pusher = new FakePusher { Fail = true };
        Assert.Equal(UpsertResult.Updated, await Store(new FakeInner(UpsertResult.Updated), pusher).UpsertAsync(LoiMoi, default));
    }

    [Fact]
    public async Task Upsert_hong_thi_nem_va_khong_day()
    {
        var pusher = new FakePusher();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store(new FakeInner(UpsertResult.Created) { Fail = true }, pusher).UpsertAsync(LoiMoi, default));
        Assert.Empty(pusher.Pushed);
    }

    private sealed class FakeInner(UpsertResult result) : INotificationStore
    {
        public bool Fail { get; init; }

        public bool Done { get; private set; }

        public Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct)
        {
            if (Fail)
                throw new InvalidOperationException("Upsert hỏng.");
            Done = true;
            return Task.FromResult(result);
        }
    }

    private sealed class FakePusher : INotificationPusher
    {
        public bool Fail { get; init; }

        public List<(Guid Recipient, NotificationUpsertedEvent Event)> Pushed { get; } = [];

        public Task PushAsync(Guid recipientId, NotificationUpsertedEvent notification, CancellationToken ct)
        {
            if (Fail)
                throw new InvalidOperationException("Hub hỏng.");
            Pushed.Add((recipientId, notification));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueries : INotificationQueries
    {
        private static readonly DateTimeOffset T = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

        public Task<NotificationRow?> FindGroupAsync(Guid recipientId, string groupKey, CancellationToken ct) =>
            Task.FromResult<NotificationRow?>(recipientId == An && groupKey == LoiMoi.GroupKey
                ? new NotificationRow(Guid.NewGuid(), "friend_request", "user", Binh, null, Binh, 1, null, false, T, T)
                : null);

        public Task<int> CountUnreadAsync(Guid recipientId, CancellationToken ct) => Task.FromResult(recipientId == An ? 7 : 0);

        public Task<IReadOnlyList<NotificationRow>> ListAsync(Guid recipientId, NotificationCursor? after, int take, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> MarkReadAsync(Guid recipientId, Guid notificationId, CancellationToken ct) => throw new NotSupportedException();

        public Task MarkAllReadAsync(Guid recipientId, DateTimeOffset upTo, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeDirectory : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserCard>>(new Dictionary<Guid, UserCard> { [Binh] = new(Binh, "Bình", null) });
    }

    private sealed class FakeStorage : IObjectStorage
    {
        public string CreatePresignedPut(string key, string contentType, long contentLength) => throw new NotSupportedException();

        public string CreatePresignedGet(string key) => "signed:" + key;

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
