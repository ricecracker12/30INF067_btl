namespace SocialApp.Modules.Messaging.Domain;

/// <summary>Loại biên nhận client gửi lên (<c>SendReceipt</c>, <c>POST …/receipts</c>). Chuỗi thường trên dây: <c>delivered</c> | <c>seen</c>.</summary>
public enum ReceiptKind
{
    Delivered,
    Seen,
}

/// <summary>Trạng thái của MỘT tin do mình gửi, nhìn từ mốc của người nhận (Đ-5.6). <c>Failed</c> chỉ tồn tại ở client.</summary>
public enum MessageDeliveryState
{
    Sent,
    Delivered,
    Seen,
}

/// <summary>
/// Mốc biên nhận tích lũy của MỘT thành viên (Đ-5.6): "đã nhận / đã xem mọi tin có <c>seq</c> ≤ N". Bất biến
/// <c>Seen ≤ Delivered ≤ seq_counter</c> — lưới cuối là <c>ck_conversations_marks</c>.
/// </summary>
public readonly record struct ReceiptMarks(long DeliveredSeq, long SeenSeq)
{
    /// <summary>
    /// Mốc mới sau một biên nhận: chỉ tăng (<c>GREATEST</c>), <paramref name="upToSeq"/> bị kẹp ở <paramref name="seqCounter"/>
    /// (nhận qua hub trước khi transaction của tin khác xong là chuyện bình thường, không phải lỗi), và đã xem kéo theo đã nhận.
    /// Cùng công thức với câu UPDATE của store — test đồng thời so hai bên.
    /// </summary>
    public ReceiptMarks Apply(ReceiptKind kind, long upToSeq, long seqCounter)
    {
        var n = Math.Min(Math.Max(upToSeq, 0), seqCounter);
        var seen = kind == ReceiptKind.Seen ? Math.Max(SeenSeq, n) : SeenSeq;
        var delivered = Math.Max(Math.Max(DeliveredSeq, n), seen);
        return new ReceiptMarks(delivered, seen);
    }

    /// <summary>Trạng thái của tin <paramref name="seq"/> do người KIA gửi cho chủ của mốc này.</summary>
    public MessageDeliveryState StateOf(long seq) =>
        seq <= SeenSeq ? MessageDeliveryState.Seen
        : seq <= DeliveredSeq ? MessageDeliveryState.Delivered
        : MessageDeliveryState.Sent;

    /// <summary>
    /// Số tin chưa đọc (Đ-5.14): <c>seq_counter − seen_seq</c>. Đúng bằng số tin của NGƯỜI KIA chưa xem, vì gửi tin tự đẩy
    /// mốc đã xem của chính người gửi lên <c>seq</c> vừa cấp.
    /// </summary>
    public long UnreadCount(long seqCounter) => Math.Max(seqCounter - SeenSeq, 0);
}
