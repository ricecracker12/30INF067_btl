using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// D6 — ma trận BR-02 (<c>READ-02..05</c>, Mục 7.4) ở tầng unit: mười hai tổ hợp
/// <c>privacy × (tác giả | người lạ) × areFriends</c> kiểm được bằng một hàm thuần, rẻ hơn hẳn mười hai lượt HTTP.
/// Integration chỉ cần vài ca chứng minh hàm này thật sự được nối vào đường đọc.
/// </summary>
public sealed class PostVisibilityTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    /// <summary>
    /// Ma trận đầy đủ. Viết tay từng dòng theo Mục 7.4, <b>không</b> sinh bằng vòng lặp gọi lại chính
    /// <see cref="PostVisibility.CanView"/> — test dùng chung nguồn với code thì code sai kiểu gì test cũng sai theo.
    /// </summary>
    [Theory]
    // public: ai cũng xem được, kể cả người lạ, bất kể quan hệ bạn bè.
    [InlineData(PostPrivacy.Public, false, false, true)]
    [InlineData(PostPrivacy.Public, false, true, true)]
    [InlineData(PostPrivacy.Public, true, false, true)]
    // private: CHỈ tác giả. areFriends không cứu được người lạ — đó là cả điểm của `private`.
    [InlineData(PostPrivacy.Private, true, false, true)]
    [InlineData(PostPrivacy.Private, false, false, false)]
    [InlineData(PostPrivacy.Private, false, true, false)]
    // friends: tác giả luôn xem được; người lạ chỉ khi là bạn.
    [InlineData(PostPrivacy.Friends, true, false, true)]
    [InlineData(PostPrivacy.Friends, false, true, true)]
    [InlineData(PostPrivacy.Friends, false, false, false)]
    public void Ma_tran_BR02_theo_Muc_7_4(PostPrivacy privacy, bool isAuthor, bool areFriends, bool expected)
    {
        var actor = isAuthor ? Author : Stranger;

        Assert.Equal(expected, PostVisibility.CanView(privacy, Author, actor, areFriends));
    }

    /// <summary>
    /// Ca dễ mất nhất khi ai đó "dọn" nhánh <c>Friends</c>: tác giả xem bài <c>friends</c> của CHÍNH MÌNH.
    ///
    /// <c>AlwaysStrangers</c> trả <c>false</c> kể cả khi hai id bằng nhau (bản thân mình không phải "bạn" của mình), nên
    /// rút gọn nhánh đó thành <c>areFriends</c> là tác giả mất quyền xem bài của chính họ — và ở GĐ2, khi mọi quan hệ
    /// đều <c>false</c>, bài <c>friends</c> sẽ không ai xem được, kể cả người viết ra nó.
    /// </summary>
    [Fact]
    public void Tac_gia_luon_xem_duoc_bai_friends_cua_chinh_minh_du_areFriends_false() =>
        Assert.True(PostVisibility.CanView(PostPrivacy.Friends, Author, Author, areFriends: false));

    /// <summary>
    /// Giá trị enum ngoài ba giá trị hợp đồng (cột DB bị sửa tay, hay enum thêm thành viên ở GĐ sau mà quên nhánh) →
    /// KHÔNG cho xem. Mặc định ĐÓNG: thêm một mức riêng tư mới mà quên sửa đây thì bài bị ẩn nhầm — khó chịu nhưng sửa
    /// được; mặc định mở thì bài bị lộ, và không ai biết.
    /// </summary>
    [Fact]
    public void Gia_tri_privacy_la_thi_khong_cho_xem() =>
        Assert.False(PostVisibility.CanView((PostPrivacy)99, Author, Author, areFriends: true));
}
