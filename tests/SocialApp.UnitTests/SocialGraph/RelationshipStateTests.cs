using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.UnitTests.SocialGraph;

/// <summary>
/// A1 — bốn nhánh nút quan hệ trên hồ sơ (Đ-4.16) + canh người ngoài cặp (Mục 6.2).
/// </summary>
public sealed class RelationshipStateTests
{
    private static readonly Guid UserA = Guid.Parse("01900000-0000-7000-8000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("01900000-0000-7000-8000-00000000000b");
    private static readonly Guid UserC = Guid.Parse("01900000-0000-7000-8000-00000000000c");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-21T12:00:00Z");

    [Fact]
    public void Null_thi_None() =>
        Assert.Equal(FriendshipView.None, RelationshipState.Of(null, UserA));

    [Fact]
    public void Accepted_thi_Friends_tu_ca_hai_phia()
    {
        var friendship = Friendship.Request(UserA, UserB, Now);
        friendship.Status = FriendshipStatus.Accepted;
        friendship.AcceptedAt = Now;

        Assert.Equal(FriendshipView.Friends, RelationshipState.Of(friendship, UserA));
        Assert.Equal(FriendshipView.Friends, RelationshipState.Of(friendship, UserB));
    }

    [Fact]
    public void Pending_thi_Outgoing_phia_nguoi_gui_Incoming_phia_nguoi_nhan()
    {
        var friendship = Friendship.Request(UserA, UserB, Now);

        Assert.Equal(FriendshipView.Outgoing, RelationshipState.Of(friendship, UserA));
        Assert.Equal(FriendshipView.Incoming, RelationshipState.Of(friendship, UserB));
    }

    [Fact]
    public void Nguoi_ngoai_cap_nem_ArgumentException()
    {
        var friendship = Friendship.Request(UserB, UserC, Now);

        var ex = Assert.Throws<ArgumentException>(() => RelationshipState.Of(friendship, UserA));

        Assert.Equal("actorId", ex.ParamName);
    }
}
