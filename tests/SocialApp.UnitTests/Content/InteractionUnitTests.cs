using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Comments;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;
using Xunit;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// C1 + D0 + mapper GĐ3 (Mục 10.3): bảng bốn nhánh của cảm xúc, cursor keyset dùng chung, và nhánh "không còn hiển thị" của
/// mapper bình luận — ba thứ thuần, test được không cần Postgres.
/// </summary>
public sealed class InteractionUnitTests
{
    // ---- C1: ReactionTransition (Mục 7.4) ----

    [Fact]
    public void Chua_co_ma_PUT_thi_INSERT_va_cong_mot()
    {
        Assert.Equal(new ReactionChange(ReactionWrite.Insert, null, ReactionType.Like),
            ReactionTransition.Apply(null, ReactionType.Like));
    }

    [Fact]
    public void Doi_loai_thi_UPDATE_tru_loai_cu_cong_loai_moi()
    {
        Assert.Equal(new ReactionChange(ReactionWrite.Update, ReactionType.Like, ReactionType.Love),
            ReactionTransition.Apply(ReactionType.Like, ReactionType.Love));
    }

    /// <summary>Đ-3.7: PUT cùng loại hai lần là idempotent — không ghi, không đổi bộ đếm.</summary>
    [Fact]
    public void PUT_cung_loai_thi_khong_lam_gi()
    {
        Assert.Equal(new ReactionChange(ReactionWrite.None, null, null),
            ReactionTransition.Apply(ReactionType.Haha, ReactionType.Haha));
    }

    [Fact]
    public void Co_roi_ma_DELETE_thi_xoa_dong_va_tru_mot()
    {
        Assert.Equal(new ReactionChange(ReactionWrite.Delete, ReactionType.Sad, null),
            ReactionTransition.Apply(ReactionType.Sad, null));
    }

    /// <summary>Đ-3.7: DELETE khi chưa thả vẫn 200 — nhánh "không làm gì", không phải lỗi.</summary>
    [Fact]
    public void Chua_co_ma_DELETE_thi_khong_lam_gi()
    {
        Assert.Equal(new ReactionChange(ReactionWrite.None, null, null), ReactionTransition.Apply(null, null));
    }

    /// <summary>Mọi cặp (cũ, mới): tổng delta bằng (mới có ? 1 : 0) − (cũ có ? 1 : 0), và −1/+1 không bao giờ cùng một loại.</summary>
    [Fact]
    public void Moi_cap_delta_bao_toan_tong_so_dong()
    {
        ReactionType?[] all = [null, .. Enum.GetValues<ReactionType>().Cast<ReactionType?>()];
        foreach (var from in all)
            foreach (var to in all)
            {
                var change = ReactionTransition.Apply(from, to);
                var delta = (change.Increment is null ? 0 : 1) - (change.Decrement is null ? 0 : 1);
                Assert.Equal((to is null ? 0 : 1) - (from is null ? 0 : 1), delta);
                if (change.Increment is not null)
                    Assert.NotEqual(change.Decrement, change.Increment);
            }
    }

    // ---- D0: KeysetCursor ----

    [Fact]
    public void KeysetCursor_round_trip_giu_nguyen_tick_va_id()
    {
        var original = new KeysetCursor(new DateTimeOffset(2026, 9, 24, 8, 30, 0, TimeSpan.Zero).AddTicks(1234567), Guid.NewGuid());

        Assert.True(KeysetCursor.TryDecode(original.Encode(), out var decoded));
        Assert.Equal(original.CreatedAt.UtcTicks, decoded.CreatedAt.UtcTicks);
        Assert.Equal(original.Id, decoded.Id);
    }

    /// <summary>Tách KeysetCursor không được đổi định dạng trên dây: cursor bài phát ra trước GĐ3 vẫn giải mã được, và ngược lại.</summary>
    [Fact]
    public void KeysetCursor_va_PostCursor_cung_mot_dinh_dang()
    {
        var at = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        Assert.Equal(new PostCursor(at, id).Encode(), new KeysetCursor(at, id).Encode());
        Assert.True(PostCursor.TryDecode(new KeysetCursor(at, id).Encode(), out var post));
        Assert.Equal(id, post.PostId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rác")]
    [InlineData("a")]
    [InlineData("bWFuZ2dvfGtoaW5n")]   // "manggo|khing" — đúng base64, sai nội dung
    public void KeysetCursor_rac_thi_false_khong_nem(string? raw)
    {
        Assert.Null(Record.Exception(() => KeysetCursor.TryDecode(raw, out _)));
        Assert.False(KeysetCursor.TryDecode(raw, out _));
    }

    // ---- Mapper bình luận (Đ-3.5, Mục 8.1) ----

    private static readonly CommentResponseMapper Mapper =
        new(new SigningOnlyStorage(), NullLogger<CommentResponseMapper>.Instance);

    [Theory]
    [InlineData(CommentStatus.Deleted)]
    [InlineData(CommentStatus.Hidden)]
    public void Binh_luan_khong_con_hien_thi_khong_lo_author_body_hay_cam_xuc(CommentStatus status)
    {
        var author = Guid.NewGuid();
        var comment = Comment.CreateRoot(Guid.NewGuid(), author, "Nội dung bí mật");
        comment.Status = status;
        comment.ReplyCount = 2;
        comment.ReactionCounts["like"] = 4;

        var response = Mapper.ToResponse(comment, new UserCard(author, "An", "avatars/a.jpg"), ReactionType.Like, author);

        Assert.Null(response.Author);
        Assert.Null(response.Body);
        Assert.Empty(response.ReactionCounts);
        Assert.Null(response.MyReaction);
        Assert.False(response.CanDelete);
        Assert.Equal(2, response.ReplyCount);   // "Xem 2 phản hồi" vẫn hiện dưới "Bình luận đã bị xóa"
        Assert.Equal(status, response.Status);
    }

    [Fact]
    public void Binh_luan_hien_thi_co_du_truong_va_canDelete_theo_nguoi_goi()
    {
        var author = Guid.NewGuid();
        var comment = Comment.CreateRoot(Guid.NewGuid(), author, "Xin chào");

        var own = Mapper.ToResponse(comment, new UserCard(author, "An", null), ReactionType.Wow, author);
        var other = Mapper.ToResponse(comment, new UserCard(author, "An", null), null, Guid.NewGuid());

        Assert.Equal("Xin chào", own.Body);
        Assert.Equal("An", own.Author!.DisplayName);
        Assert.Equal(ReactionType.Wow, own.MyReaction);
        Assert.True(own.CanDelete);
        Assert.False(other.CanDelete);
    }

    private sealed class SigningOnlyStorage : IObjectStorage
    {
        public string CreatePresignedGet(string key) => $"https://ky.invalid/{key}";

        public string CreatePresignedPut(string key, string contentType, long contentLength) => throw new NotSupportedException();

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
