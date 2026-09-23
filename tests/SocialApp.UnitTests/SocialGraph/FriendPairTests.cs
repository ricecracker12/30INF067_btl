using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.UnitTests.SocialGraph;

/// <summary>
/// A1 — <see cref="FriendPair.Of"/> phải khớp thứ tự uuid của Postgres (GUID-01).
/// Cặp đối nghịch: so chuỗi hex (Postgres) và so <c>ToByteArray()</c> cho kết quả ngược nhau.
/// </summary>
public sealed class FriendPairTests
{
    // Nhóm đầu khác endian: hex hiển thị a < b, nhưng byte đầu của a là 01 còn b là 00.
    private static readonly Guid OppositeA = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid OppositeB = Guid.Parse("01000000-0000-0000-0000-000000000000");

    [Fact]
    public void Of_giao_hoan_cung_mot_cap()
    {
        var forward = FriendPair.Of(OppositeA, OppositeB);
        var reverse = FriendPair.Of(OppositeB, OppositeA);

        Assert.Equal(forward, reverse);
        Assert.Equal(OppositeA, forward.Min);
        Assert.Equal(OppositeB, forward.Max);
    }

    /// <summary>
    /// Lưới GUID-01: so hex Postgres → a &lt; b; so ToByteArray() → b &lt; a.
    /// Chuẩn hóa bằng mảng byte sẽ đặt Min = b và vi phạm ck_friendships_order khi INSERT.
    /// </summary>
    [Fact]
    public void Cap_doi_nghich_Min_la_a_theo_thu_tu_Postgres()
    {
        Assert.True(OppositeA.CompareTo(OppositeB) < 0, "Guid.CompareTo phải khớp hex Postgres");
        Assert.True(
            OppositeA.ToByteArray()[0] > OppositeB.ToByteArray()[0],
            "ToByteArray phải đảo chiều ở byte đầu — nếu không, cặp này hết giá trị lưới");

        var pair = FriendPair.Of(OppositeA, OppositeB);

        Assert.Equal(OppositeA, pair.Min);
        Assert.Equal(OppositeB, pair.Max);
    }

    [Fact]
    public void Of_hai_id_bang_nhau_nem_ArgumentException()
    {
        var id = Guid.Parse("01900000-0000-7000-8000-000000000001");

        var ex = Assert.Throws<ArgumentException>(() => FriendPair.Of(id, id));

        Assert.Equal("b", ex.ParamName);
    }

    /// <summary>
    /// <see cref="FriendPair.Of"/> là đường DUY NHẤT tạo cặp. Đổi lại thành record positional là mở
    /// <c>new FriendPair(a, b)</c> — chuẩn hóa sai chiều với khoảng một nửa số cặp, lộ ra dưới dạng 23514.
    /// </summary>
    [Fact]
    public void Khong_co_constructor_public()
    {
        Assert.Empty(typeof(FriendPair).GetConstructors());
    }

    [Fact]
    public void Contains_va_Other()
    {
        var pair = FriendPair.Of(OppositeA, OppositeB);

        Assert.True(pair.Contains(OppositeA));
        Assert.True(pair.Contains(OppositeB));
        Assert.False(pair.Contains(Guid.NewGuid()));

        Assert.Equal(OppositeB, pair.Other(OppositeA));
        Assert.Equal(OppositeA, pair.Other(OppositeB));
        Assert.Throws<ArgumentException>(() => pair.Other(Guid.NewGuid()));
    }
}
