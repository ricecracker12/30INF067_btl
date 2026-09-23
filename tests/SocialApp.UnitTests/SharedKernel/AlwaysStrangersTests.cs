using SocialApp.SharedKernel.Contracts;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// <b>Test pin</b> cho <see cref="AlwaysStrangers"/> (A6, Đ-2.9): null-object của GĐ2 luôn trả <c>false</c>.
///
/// GĐ4 (Đ-4.3) đăng ký hiện thực thật ở SocialGraph và xóa dòng AlwaysStrangers ở Content —
/// KHÔNG sửa test này. Test này còn xanh sau GĐ4 là đúng: nó nói về <see cref="AlwaysStrangers"/>,
/// không nói về "kết bạn ở hệ thống này hoạt động ra sao".
///
/// Lý do cần pin: đổi thân hàm thành <c>true</c> "cho dễ test" là mở toàn bộ bài chế độ <c>friends</c> cho
/// mọi người, và không có test nào khác nhìn thấy hành vi của chính class null-object.
/// </summary>
public sealed class AlwaysStrangersTests
{
    private readonly IFriendshipReader _reader = new AlwaysStrangers();

    [Fact]
    public async Task Hai_nguoi_khac_nhau_khong_phai_ban()
    {
        Assert.False(await _reader.AreFriendsAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    /// <summary>
    /// Cùng một id cũng trả <c>false</c>: bản thân mình không phải "bạn" của mình. Đường tác giả xem bài của
    /// chính mình đi qua nhánh <c>author_id == actorId</c> ở D6 — trả <c>true</c> ở đây là trộn hai khái
    /// niệm và làm BR-02 khó đọc.
    /// </summary>
    [Fact]
    public async Task Cung_mot_nguoi_cung_khong_phai_ban()
    {
        var me = Guid.NewGuid();

        Assert.False(await _reader.AreFriendsAsync(me, me));
    }
}
