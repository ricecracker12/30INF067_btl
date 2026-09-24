using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.UnitTests.Messaging;

/// <summary>
/// A1 — <see cref="ConversationPair.Of"/> phải khớp thứ tự uuid của Postgres (Đ-5.2), cùng lưới với <c>FriendPairTests</c>
/// của GĐ4. Cặp đối nghịch: so chuỗi hex (Postgres) và so <c>ToByteArray()</c> cho kết quả ngược nhau. Lưới thật với Postgres
/// là <c>PAIR-01</c> ở IntegrationTests.
/// </summary>
public sealed class ConversationPairTests
{
    private static readonly Guid OppositeA = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid OppositeB = Guid.Parse("01000000-0000-0000-0000-000000000000");

    [Fact]
    public void Of_giao_hoan_cung_mot_cap()
    {
        Assert.Equal(ConversationPair.Of(OppositeA, OppositeB), ConversationPair.Of(OppositeB, OppositeA));
    }

    [Fact]
    public void Cap_doi_nghich_UserA_la_a_theo_thu_tu_Postgres()
    {
        Assert.True(
            OppositeA.ToByteArray()[0] > OppositeB.ToByteArray()[0],
            "ToByteArray phải đảo chiều ở byte đầu — nếu không, cặp này hết giá trị lưới");

        var pair = ConversationPair.Of(OppositeB, OppositeA);

        Assert.Equal(OppositeA, pair.UserA);
        Assert.Equal(OppositeB, pair.UserB);
    }

    [Fact]
    public void Hai_id_bang_nhau_nem_ArgumentException()
    {
        var id = Guid.Parse("01900000-0000-7000-8000-000000000001");

        Assert.Equal("b", Assert.Throws<ArgumentException>(() => ConversationPair.Of(id, id)).ParamName);
    }

    [Fact]
    public void Khong_co_constructor_public()
    {
        Assert.Empty(typeof(ConversationPair).GetConstructors());
    }
}
