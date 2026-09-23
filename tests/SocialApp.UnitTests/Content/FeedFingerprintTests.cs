using SocialApp.Modules.Content.Application.Feed;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// C4 — dấu nguồn của cache trang đầu (Q-C1). Ba tính chất mà <c>FeedService</c> dựa vào: ổn định theo NỘI DUNG (không theo
/// thứ tự của set), đổi khi nguồn đổi, và phân biệt hai tập (bạn ↔ chỉ theo dõi).
/// </summary>
public sealed class FeedFingerprintTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static FeedSources Of(Guid[] friends, Guid[] followingOnly) =>
        new(new HashSet<Guid>(friends), new HashSet<Guid>(followingOnly));

    [Fact]
    public void Cung_noi_dung_khac_thu_tu_them_vao_thi_cung_dau() =>
        Assert.Equal(FeedFingerprint.Of(Of([A, B], [C])), FeedFingerprint.Of(Of([B, A], [C])));

    [Fact]
    public void Them_mot_ban_thi_doi_dau() =>
        Assert.NotEqual(FeedFingerprint.Of(Of([A], [])), FeedFingerprint.Of(Of([A, B], [])));

    /// <summary>
    /// Hủy kết bạn khi vẫn theo dõi: B chuyển từ Friends sang FollowingOnly, HỢP hai tập không đổi — dấu vẫn phải đổi, không
    /// thì trang đầu đã cache (có bài <c>friends</c> của B) còn trúng tới 30s.
    /// </summary>
    [Fact]
    public void Ban_chuyen_sang_chi_theo_doi_thi_doi_dau() =>
        Assert.NotEqual(FeedFingerprint.Of(Of([A, B], [])), FeedFingerprint.Of(Of([A], [B])));

    /// <summary>Người mới (nguồn rỗng) có dấu cố định — trang gợi ý cache được, và đổi ngay khi có kết nối đầu tiên.</summary>
    [Fact]
    public void Nguon_rong_on_dinh_va_khac_nguon_co_mot_ket_noi()
    {
        var empty = FeedFingerprint.Of(Of([], []));

        Assert.Equal(empty, FeedFingerprint.Of(Of([], [])));
        Assert.NotEqual(empty, FeedFingerprint.Of(Of([], [A])));
    }

    /// <summary>16 byte base64 = 24 ký tự: giá trị cache giữ ngắn, không phình theo số nguồn.</summary>
    [Fact]
    public void Do_dai_co_dinh_khong_phu_thuoc_so_nguon()
    {
        var many = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray();

        Assert.Equal(24, FeedFingerprint.Of(Of([], [])).Length);
        Assert.Equal(24, FeedFingerprint.Of(Of(many, [])).Length);
    }
}
