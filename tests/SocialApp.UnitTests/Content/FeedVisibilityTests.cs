using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// C2 — bảng Đ-4.5 (ba nguồn × ba mức × <c>published</c>/<c>hidden</c>) và điều kiện gợi ý Đ-4.6 ở tầng unit. Bản SQL của
/// cùng luật được đối chiếu ở <c>FeedStoreTests</c> (integration) — test này chỉ chốt bảng.
/// </summary>
public sealed class FeedVisibilityTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Friend = Guid.NewGuid();
    private static readonly Guid Followee = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FeedSources Sources = new(
        new HashSet<Guid> { Friend },
        new HashSet<Guid> { Followee });

    public enum Who { Me, Friend, Followee, Stranger }

    private static Guid IdOf(Who who) => who switch
    {
        Who.Me => Me,
        Who.Friend => Friend,
        Who.Followee => Followee,
        _ => Stranger,
    };

    /// <summary>Viết tay từng dòng theo bảng Đ-4.5 — không sinh bằng vòng lặp gọi lại chính hàm cần kiểm.</summary>
    [Theory]
    // Chính mình: mọi mức.
    [InlineData(Who.Me, PostPrivacy.Public, true)]
    [InlineData(Who.Me, PostPrivacy.Friends, true)]
    [InlineData(Who.Me, PostPrivacy.Private, true)]
    // Bạn bè: public + friends, KHÔNG private.
    [InlineData(Who.Friend, PostPrivacy.Public, true)]
    [InlineData(Who.Friend, PostPrivacy.Friends, true)]
    [InlineData(Who.Friend, PostPrivacy.Private, false)]
    // Chỉ theo dõi: CHỈ public.
    [InlineData(Who.Followee, PostPrivacy.Public, true)]
    [InlineData(Who.Followee, PostPrivacy.Friends, false)]
    [InlineData(Who.Followee, PostPrivacy.Private, false)]
    // Người lạ: không gì cả — kể cả public (feed mạng lưới không trộn gợi ý, Đ-4.6).
    [InlineData(Who.Stranger, PostPrivacy.Public, false)]
    [InlineData(Who.Stranger, PostPrivacy.Friends, false)]
    [InlineData(Who.Stranger, PostPrivacy.Private, false)]
    public void Bang_D4_5_bai_published(Who author, PostPrivacy privacy, bool expected) =>
        Assert.Equal(expected, FeedVisibility.CanSee(privacy, PostStatus.Published, IdOf(author), Me, Sources));

    /// <summary>BR-07: <c>hidden</c> không lên feed với BẤT KỲ ai — kể cả tác giả, kể cả bài <c>public</c>.</summary>
    [Theory]
    [InlineData(Who.Me, PostPrivacy.Public)]
    [InlineData(Who.Me, PostPrivacy.Private)]
    [InlineData(Who.Friend, PostPrivacy.Public)]
    [InlineData(Who.Friend, PostPrivacy.Friends)]
    [InlineData(Who.Followee, PostPrivacy.Public)]
    public void Bai_hidden_khong_ai_thay(Who author, PostPrivacy privacy) =>
        Assert.False(FeedVisibility.CanSee(privacy, PostStatus.Hidden, IdOf(author), Me, Sources));

    /// <summary>Mức riêng tư lạ → mặc định đóng, kể cả với bạn bè.</summary>
    [Fact]
    public void Privacy_la_thi_khong_thay() =>
        Assert.False(FeedVisibility.CanSee((PostPrivacy)99, PostStatus.Published, Friend, Me, Sources));

    /// <summary>
    /// Đ-4.6 (sửa 2026-09-23): bài public đã đăng của người khác, CỘNG bài đã đăng của chính mình mọi mức; của người khác thì
    /// không friends/private; không hidden — kể cả của mình (BR-07).
    /// </summary>
    [Theory]
    [InlineData(false, PostPrivacy.Public, PostStatus.Published, true)]
    [InlineData(true, PostPrivacy.Public, PostStatus.Published, true)]
    [InlineData(true, PostPrivacy.Friends, PostStatus.Published, true)]
    [InlineData(true, PostPrivacy.Private, PostStatus.Published, true)]
    [InlineData(true, PostPrivacy.Public, PostStatus.Hidden, false)]
    [InlineData(false, PostPrivacy.Friends, PostStatus.Published, false)]
    [InlineData(false, PostPrivacy.Private, PostStatus.Published, false)]
    [InlineData(false, PostPrivacy.Public, PostStatus.Hidden, false)]
    public void Feed_goi_y_D4_6(bool isMine, PostPrivacy privacy, PostStatus status, bool expected) =>
        Assert.Equal(expected, FeedVisibility.CanSeeSuggested(privacy, status, isMine ? Me : Stranger, Me));
}
