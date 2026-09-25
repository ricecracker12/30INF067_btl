using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Application.Handlers;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.UnitTests.Notification;

/// <summary>
/// GĐ6 D10 — phép dịch event → thông báo của ba handler "làm ngay" (Đ-6.17). Mọi id là <see cref="Guid"/>: đổi chỗ người mời với người
/// được mời vẫn compile, và thông báo tới nhầm người. Kỳ vọng viết tay bằng chuỗi hợp đồng, không đọc lại <c>NotificationTypes</c> /
/// <c>GroupKey</c> — test dùng chung nguồn với code thì code sai kiểu gì test cũng sai theo.
///
/// Đường thật (API của module phát → bus → store → bảng) ở <c>NotificationHandlerTests</c> của IntegrationTests.
/// </summary>
public sealed class NotificationHandlerTests
{
    private static readonly Guid An = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Binh = Guid.Parse("01900000-0000-7000-8000-0000000000b2");
    private static readonly Guid Bai = Guid.Parse("01900000-0000-7000-8000-0000000000c3");

    [Fact]
    public async Task Loi_moi_ket_ban_toi_nguoi_duoc_moi_actor_la_nguoi_moi()
    {
        var store = new RecordingStore();
        await new FriendRequestSentHandler(store).HandleAsync(new FriendRequestSent(RequesterId: An, AddresseeId: Binh), default);

        Assert.Equal(
            new NotificationUpsert(Binh, "friend_request", $"friend_request:{An:D}", "user", An, null, An, null),
            Assert.Single(store.Upserts));
    }

    [Fact]
    public async Task Chap_nhan_toi_nguoi_da_gui_loi_moi_actor_la_nguoi_bam()
    {
        var store = new RecordingStore();
        await new FriendRequestAcceptedHandler(store).HandleAsync(new FriendRequestAccepted(RequesterId: An, AccepterId: Binh), default);

        Assert.Equal(
            new NotificationUpsert(An, "friend_accepted", $"friend_accepted:{Binh:D}", "user", Binh, null, Binh, null),
            Assert.Single(store.Upserts));
    }

    /// <summary>B.10 #7: không actor, có lý do; đích và bài dẫn tới lấy từ event.</summary>
    [Fact]
    public async Task Noi_dung_bi_an_toi_tac_gia_khong_actor_co_ly_do()
    {
        var store = new RecordingStore();
        await new ContentHiddenHandler(store).HandleAsync(
            new ContentHidden(ModerationTargetType.Post, Bai, Bai, AuthorId: An, ReasonCode: "violence"), default);

        Assert.Equal(
            new NotificationUpsert(An, "moderation", $"moderation:post:{Bai:D}", "post", Bai, Bai, null, "violence"),
            Assert.Single(store.Upserts));
    }

    [Fact]
    public async Task Tai_khoan_bi_an_dich_la_user_khong_co_bai()
    {
        var store = new RecordingStore();
        await new ContentHiddenHandler(store).HandleAsync(
            new ContentHidden(ModerationTargetType.User, Binh, null, AuthorId: Binh, ReasonCode: "spam"), default);

        Assert.Equal(
            new NotificationUpsert(Binh, "moderation", $"moderation:user:{Binh:D}", "user", Binh, null, null, "spam"),
            Assert.Single(store.Upserts));
    }

    /// <summary>Handler KHÔNG nuốt lỗi của store — bus (C0) là chỗ bắt và log (<c>EVT-02</c>).</summary>
    [Fact]
    public async Task Loi_cua_store_thoat_ra_khoi_handler()
    {
        var store = new RecordingStore { Fail = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FriendRequestSentHandler(store).HandleAsync(new FriendRequestSent(An, Binh), default));
    }

    private sealed class RecordingStore : INotificationStore
    {
        public List<NotificationUpsert> Upserts { get; } = [];

        public bool Fail { get; init; }

        public Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct)
        {
            if (Fail)
                throw new InvalidOperationException("Store hỏng.");
            Upserts.Add(upsert);
            return Task.FromResult(UpsertResult.Created);
        }
    }
}
