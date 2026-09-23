using SocialApp.Modules.SocialGraph.Application.Relationships;

namespace SocialApp.UnitTests.SocialGraph;

/// <summary>
/// D5 — <see cref="FriendCursor"/>. Cùng lưới <c>PostCursorTests</c>: không ném với rác, round-trip giữ tick,
/// chuỗi an toàn trong query, offset khác 0 quy về UTC.
/// </summary>
public sealed class FriendCursorTests
{
    [Fact]
    public void Round_trip_giu_nguyen_tick_va_id()
    {
        var original = new FriendCursor(
            new DateTimeOffset(2026, 9, 22, 11, 22, 33, TimeSpan.Zero).AddTicks(4567891),
            Guid.NewGuid());

        Assert.True(FriendCursor.TryDecode(original.Encode(), out var decoded));
        Assert.Equal(original.Since.UtcTicks, decoded.Since.UtcTicks);
        Assert.Equal(original.OtherUserId, decoded.OtherUserId);
    }

    [Fact]
    public void Chuoi_ma_hoa_an_toan_voi_URL()
    {
        for (var i = 0; i < 200; i++)
        {
            var encoded = new FriendCursor(DateTimeOffset.UtcNow.AddTicks(i), Guid.NewGuid()).Encode();

            Assert.DoesNotContain('+', encoded);
            Assert.DoesNotContain('/', encoded);
            Assert.DoesNotContain('=', encoded);
            Assert.Equal(encoded, Uri.EscapeDataString(encoded));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("!!!không phải base64!!!")]
    [InlineData("eA")]
    [InlineData("eHx5")]
    [InlineData("MjAyNi0wMS0wMXxub3QtYS1ndWlk")]
    [InlineData("fHw=")]
    public void Rac_tra_false_va_khong_nem(string? raw)
    {
        var thrown = Record.Exception(() => FriendCursor.TryDecode(raw, out _));

        Assert.Null(thrown);
        Assert.False(FriendCursor.TryDecode(raw, out _));
    }

    [Fact]
    public void Cursor_mang_offset_khac_0_van_giai_ma_duoc_va_quy_ve_UTC()
    {
        var saiGon = new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.FromHours(7));
        var other = Guid.NewGuid();
        var raw = new FriendCursor(saiGon, other).Encode();

        Assert.True(FriendCursor.TryDecode(raw, out var decoded));
        Assert.Equal(TimeSpan.Zero, decoded.Since.Offset);
        Assert.Equal(saiGon.UtcTicks, decoded.Since.UtcTicks);
        Assert.Equal(other, decoded.OtherUserId);
    }
}
