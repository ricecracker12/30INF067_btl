using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Application.Handlers;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Realtime;
using Xunit;

namespace SocialApp.UnitTests.Notification;

/// <summary>
/// Bước 9 của GĐ6 (sau khi GĐ3, GĐ5 merge) — phép dịch <c>CommentCreated</c>, <c>ReactionSet</c>, <c>MessageSent</c> → thông báo (Đ-6.17).
/// Kỳ vọng viết tay bằng chuỗi hợp đồng, như <see cref="NotificationHandlerTests"/>. Đường thật đi từ API của Content/Messaging ở
/// <c>InteractionNotificationTests</c> của IntegrationTests.
/// </summary>
public sealed class InteractionHandlerTests
{
    private static readonly Guid TacGiaBai = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid TacGiaCha = Guid.Parse("01900000-0000-7000-8000-0000000000b2");
    private static readonly Guid NguoiViet = Guid.Parse("01900000-0000-7000-8000-0000000000c3");
    private static readonly Guid Bai = Guid.Parse("01900000-0000-7000-8000-0000000000d4");
    private static readonly Guid BinhLuan = Guid.Parse("01900000-0000-7000-8000-0000000000e5");
    private static readonly Guid Cha = Guid.Parse("01900000-0000-7000-8000-0000000000f6");

    private static NotificationUpsert BinhLuanBai(Guid recipient, Guid actor) =>
        new(recipient, "comment", $"comment:post:{Bai:D}", "post", Bai, Bai, actor, null);

    private static NotificationUpsert TraLoi(Guid recipient, Guid actor) =>
        new(recipient, "reply", $"reply:comment:{Cha:D}", "comment", Cha, Bai, actor, null);

    [Fact]
    public void Binh_luan_goc_chi_bao_tac_gia_bai()
    {
        Assert.Equal(
            [BinhLuanBai(TacGiaBai, NguoiViet)],
            CommentCreatedHandler.For(new CommentCreated(BinhLuan, Bai, TacGiaBai, null, null, NguoiViet, [])));
    }

    /// <summary>Trả lời bình luận của người khác trên bài của A: người có bình luận cha nhận <c>reply</c>, A nhận <c>comment</c>.</summary>
    [Fact]
    public void Tra_loi_bao_tac_gia_cha_va_tac_gia_bai()
    {
        Assert.Equal(
            [TraLoi(TacGiaCha, NguoiViet), BinhLuanBai(TacGiaBai, NguoiViet)],
            CommentCreatedHandler.For(new CommentCreated(BinhLuan, Bai, TacGiaBai, Cha, TacGiaCha, NguoiViet, [])));
    }

    /// <summary>Trả lời bình luận CỦA CHÍNH tác giả bài: một thông báo <c>reply</c>, không kèm <c>comment</c> cho cùng người.</summary>
    [Fact]
    public void Tra_loi_binh_luan_cua_tac_gia_bai_chi_mot_thong_bao()
    {
        Assert.Equal(
            [TraLoi(TacGiaBai, NguoiViet)],
            CommentCreatedHandler.For(new CommentCreated(BinhLuan, Bai, TacGiaBai, Cha, TacGiaBai, NguoiViet, [])));
    }

    [Fact]
    public void Cam_xuc_moi_tren_bai_va_tren_binh_luan()
    {
        Assert.Equal(
            new NotificationUpsert(TacGiaBai, "reaction", $"reaction:post:{Bai:D}", "post", Bai, Bai, NguoiViet, null),
            ReactionSetHandler.For(new ReactionSet(ReactionTargetKind.Post, Bai, Bai, TacGiaBai, NguoiViet, IsNew: true)));
        Assert.Equal(
            new NotificationUpsert(TacGiaCha, "reaction", $"reaction:comment:{Cha:D}", "comment", Cha, Bai, NguoiViet, null),
            ReactionSetHandler.For(new ReactionSet(ReactionTargetKind.Comment, Cha, Bai, TacGiaCha, NguoiViet, IsNew: true)));
    }

    [Fact]
    public async Task Doi_loai_cam_xuc_khong_tao_gi()
    {
        var store = new RecordingStore();
        await new ReactionSetHandler(store).HandleAsync(
            new ReactionSet(ReactionTargetKind.Post, Bai, Bai, TacGiaBai, NguoiViet, IsNew: false), default);
        Assert.Empty(store.Upserts);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Tin_nhan_chi_bao_khi_nguoi_nhan_offline(bool online, int expected)
    {
        var hoiThoai = Guid.NewGuid();
        var store = new RecordingStore();
        var presence = new FixedPresence(online);
        await new MessageSentHandler(store, presence).HandleAsync(
            new MessageSent(hoiThoai, Guid.NewGuid(), SenderId: NguoiViet, RecipientId: TacGiaBai, Seq: 3), default);

        Assert.Equal([TacGiaBai], presence.Asked);   // hỏi presence của NGƯỜI NHẬN, không phải người gửi
        Assert.Equal(expected, store.Upserts.Count);
        if (expected == 1)
            Assert.Equal(
                new NotificationUpsert(TacGiaBai, "message", $"message:{hoiThoai:D}", "conversation", hoiThoai, null, NguoiViet, null),
                store.Upserts[0]);
    }

    private sealed class RecordingStore : INotificationStore
    {
        public List<NotificationUpsert> Upserts { get; } = [];

        public Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct)
        {
            Upserts.Add(upsert);
            return Task.FromResult(UpsertResult.Created);
        }
    }

    private sealed class FixedPresence(bool online) : IPresenceReader
    {
        public List<Guid> Asked { get; } = [];

        public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        {
            Asked.Add(userId);
            return Task.FromResult(online);
        }
    }
}
