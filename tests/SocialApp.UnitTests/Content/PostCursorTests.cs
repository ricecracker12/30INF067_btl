using SocialApp.Modules.Content.Application.Posts;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// D6 — <see cref="PostCursor"/>. Cursor là chuỗi client gửi lên nên phải coi là dữ liệu thù địch: khẳng định quan trọng
/// nhất của lớp này không phải "giải mã đúng" mà là <b>"không ném với bất kỳ đầu vào nào"</b> — một exception ở đây là
/// 500 từ một chuỗi người dùng sửa tay, trong khi hợp đồng đòi 400.
/// </summary>
public sealed class PostCursorTests
{
    /// <summary>
    /// Round-trip giữ NGUYÊN tick. <c>"O"</c> ghi 7 chữ số phần giây nên không mất độ chính xác — dùng format khác
    /// (<c>"s"</c>, <c>"u"</c>) là cắt mất microsecond, và hai bài cách nhau 0,5 µs sẽ thành cùng một mốc: trang sau lặp
    /// lại hoặc bỏ sót đúng một bài.
    /// </summary>
    [Fact]
    public void Round_trip_giu_nguyen_tick_va_id()
    {
        var original = new PostCursor(new DateTimeOffset(2026, 9, 20, 11, 22, 33, TimeSpan.Zero).AddTicks(4567891), Guid.NewGuid());

        Assert.True(PostCursor.TryDecode(original.Encode(), out var decoded));
        Assert.Equal(original.CreatedAt.UtcTicks, decoded.CreatedAt.UtcTicks);
        Assert.Equal(original.PostId, decoded.PostId);
    }

    /// <summary>
    /// Base64url: chuỗi mã hóa KHÔNG được chứa <c>+</c>, <c>/</c> hay <c>=</c> — ba ký tự phải percent-encode trong
    /// query string. Quên một chỗ là cursor hỏng sau một vòng URL, và triệu chứng (400 "cursor không hợp lệ") không nói
    /// gì về nguyên nhân. Lặp nhiều lần vì việc có sinh ra <c>+</c>/<c>/</c> hay không phụ thuộc byte của Guid.
    /// </summary>
    [Fact]
    public void Chuoi_ma_hoa_an_toan_voi_URL()
    {
        for (var i = 0; i < 200; i++)
        {
            var encoded = new PostCursor(DateTimeOffset.UtcNow.AddTicks(i), Guid.NewGuid()).Encode();

            Assert.DoesNotContain('+', encoded);
            Assert.DoesNotContain('/', encoded);
            Assert.DoesNotContain('=', encoded);
            Assert.Equal(encoded, Uri.EscapeDataString(encoded));
        }
    }

    /// <summary>
    /// Mọi loại rác → <c>false</c>, KHÔNG ném. Mỗi dòng là một cách hỏng khác nhau, không phải biến thể của cùng một
    /// cách: sai base64, đúng base64 nhưng sai cấu trúc, đúng cấu trúc nhưng sai kiểu từng phần.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]                                      // PAGE-02 — ca của hợp đồng
    [InlineData("!!!không phải base64!!!")]
    [InlineData("eA")]                                       // base64 hợp lệ của "x" — thiếu dấu '|'
    [InlineData("eHx5")]                                     // base64 của "x|y" — hai phần nhưng sai kiểu cả hai
    [InlineData("MjAyNi0wMS0wMXxub3QtYS1ndWlk")]             // ngày hợp lệ dạng khác + guid rác
    [InlineData("fHw=")]                                     // "||" — ba phần
    public void Rac_tra_false_va_khong_nem(string? raw)
    {
        var decoded = Record.Exception(() => PostCursor.TryDecode(raw, out _));

        Assert.Null(decoded);
        Assert.False(PostCursor.TryDecode(raw, out _));
    }

    /// <summary>
    /// Ngày đúng dạng <c>"O"</c> nhưng KHÁC múi giờ → giải mã được và quy về UTC.
    ///
    /// Đây là ca duy nhất trong lớp này có hậu quả ở tầng hạ tầng: Npgsql TỪ CHỐI ghi <c>DateTimeOffset</c> có
    /// <c>Offset</c> khác 0 vào <c>timestamp with time zone</c> và ném "Cannot write DateTimeOffset with
    /// Offset=07:00:00…". Thiếu <c>ToUniversalTime()</c> thì một cursor sửa tay thành <b>500</b>.
    ///
    /// <b>Lệch bảng Mục 0</b>, đã đối chiếu và chọn theo Bước 3: Mục 0 ghi ca này ra "400, không 500", nhưng đoạn mã của
    /// Bước 3 ghi rõ phải quy về UTC và không được ném — tức là <b>200</b>. Cùng một mốc thời gian, chỉ khác cách biểu
    /// diễn, nên keyset không đổi kết quả; điều bảng Mục 0 thật sự muốn là "không 500", và điều đó vẫn đúng.
    /// </summary>
    [Fact]
    public void Cursor_mang_offset_khac_0_van_giai_ma_duoc_va_quy_ve_UTC()
    {
        var saiGon = new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.FromHours(7));
        var postId = Guid.NewGuid();
        var raw = new PostCursor(saiGon, postId).Encode();

        Assert.True(PostCursor.TryDecode(raw, out var decoded));
        Assert.Equal(TimeSpan.Zero, decoded.CreatedAt.Offset);
        Assert.Equal(saiGon.UtcTicks, decoded.CreatedAt.UtcTicks);
        Assert.Equal(postId, decoded.PostId);
    }
}
