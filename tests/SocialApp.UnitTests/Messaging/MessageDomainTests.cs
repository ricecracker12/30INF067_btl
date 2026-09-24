using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.UnitTests.Messaging;

/// <summary>A1 — luật nội dung tin (<see cref="MessageContentPolicy"/>), mốc biên nhận (<see cref="ReceiptMarks"/>, Đ-5.6, Đ-5.14).</summary>
public sealed class MessageDomainTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t \r\n")]
    public void Noi_dung_rong_hoac_toan_khoang_trang_bi_chan(string? content)
    {
        Assert.Equal(MessageContentPolicy.Empty, MessageContentPolicy.Validate(content));
    }

    [Fact]
    public void Dung_2000_ky_tu_hop_le_2001_bi_chan()
    {
        Assert.Null(MessageContentPolicy.Validate(new string('a', 2000)));
        Assert.Equal(MessageContentPolicy.TooLong, MessageContentPolicy.Validate(new string('a', 2001)));
    }

    /// <summary>
    /// Emoji là 2 đơn vị UTF-16 nhưng 1 ký tự với <c>char_length</c> của Postgres. 1.500 emoji = 3.000 <c>Length</c> — đếm bằng
    /// <c>Length</c> là từ chối oan một tin DB nhận được.
    /// </summary>
    [Fact]
    public void Dem_theo_code_point_khong_theo_UTF16()
    {
        var emoji = string.Concat(Enumerable.Repeat("😀", 1500));

        Assert.Equal(3000, emoji.Length);
        Assert.Equal(1500, MessageContentPolicy.CountCharacters(emoji));
        Assert.Null(MessageContentPolicy.Validate(emoji));
        Assert.NotNull(MessageContentPolicy.Validate(string.Concat(Enumerable.Repeat("😀", 2001))));
    }

    [Fact]
    public void Moc_chi_tang_seen_5_roi_seen_3_van_la_5()
    {
        var marks = new ReceiptMarks(0, 0)
            .Apply(ReceiptKind.Seen, 5, seqCounter: 10)
            .Apply(ReceiptKind.Seen, 3, seqCounter: 10);

        Assert.Equal(new ReceiptMarks(5, 5), marks);
    }

    [Fact]
    public void UpToSeq_vuot_seq_counter_bi_kep()
    {
        Assert.Equal(new ReceiptMarks(7, 7), new ReceiptMarks(0, 0).Apply(ReceiptKind.Seen, 99, seqCounter: 7));
        Assert.Equal(new ReceiptMarks(7, 0), new ReceiptMarks(0, 0).Apply(ReceiptKind.Delivered, 99, seqCounter: 7));
    }

    [Fact]
    public void Da_xem_keo_theo_da_nhan_con_da_nhan_khong_keo_da_xem()
    {
        Assert.Equal(new ReceiptMarks(4, 4), new ReceiptMarks(2, 1).Apply(ReceiptKind.Seen, 4, 10));
        Assert.Equal(new ReceiptMarks(6, 1), new ReceiptMarks(2, 1).Apply(ReceiptKind.Delivered, 6, 10));
    }

    [Fact]
    public void Upto_am_khong_lam_giam_moc()
    {
        Assert.Equal(new ReceiptMarks(3, 2), new ReceiptMarks(3, 2).Apply(ReceiptKind.Seen, -5, 10));
    }

    [Theory]
    [InlineData(3, MessageDeliveryState.Seen)]
    [InlineData(4, MessageDeliveryState.Delivered)]
    [InlineData(6, MessageDeliveryState.Delivered)]
    [InlineData(7, MessageDeliveryState.Sent)]
    public void Suy_trang_thai_tu_moc_nguoi_nhan(long seq, MessageDeliveryState expected)
    {
        Assert.Equal(expected, new ReceiptMarks(DeliveredSeq: 6, SeenSeq: 3).StateOf(seq));
    }

    [Fact]
    public void Chua_doc_la_seq_counter_tru_seen_khong_am()
    {
        Assert.Equal(3, new ReceiptMarks(5, 2).UnreadCount(5));
        Assert.Equal(0, new ReceiptMarks(5, 5).UnreadCount(5));
        Assert.Equal(0, new ReceiptMarks(0, 9).UnreadCount(5));
    }

    [Fact]
    public void Conversation_thanh_vien_nguoi_kia_va_moc()
    {
        var a = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var b = Guid.Parse("01000000-0000-0000-0000-000000000000");
        var c = Conversation.Start(Guid.NewGuid(), ConversationPair.Of(b, a), DateTimeOffset.UnixEpoch);
        c.UserBDeliveredSeq = 4;
        c.UserBSeenSeq = 2;

        Assert.True(c.IsMember(a));
        Assert.True(c.IsMember(b));
        Assert.False(c.IsMember(Guid.NewGuid()));
        Assert.Equal(b, c.PeerOf(a));
        Assert.Equal(new ReceiptMarks(4, 2), c.MarksOf(b));
        Assert.Throws<ArgumentException>(() => c.PeerOf(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => c.MarksOf(Guid.NewGuid()));
    }
}
