using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// Truy cập dữ liệu của module (A5). Application định nghĩa, Infrastructure hiện thực bằng EF + SQL tham số hóa — chỉ
/// Infrastructure chạm EF (<c>PersistenceBoundaryTests</c>). Store KHÔNG kiểm quyền: tầng 3 là việc của
/// <see cref="ConversationAccess"/>; store chỉ đọc/ghi đúng thứ được bảo.
/// </summary>
public interface IConversationStore
{
    /// <summary>Hội thoại theo id, không theo dõi thay đổi. <c>null</c> nếu không có.</summary>
    Task<Conversation?> FindAsync(Guid conversationId, CancellationToken ct);

    /// <summary>
    /// Get-or-create theo cặp (Đ-5.2): <c>INSERT … ON CONFLICT (user_a_id, user_b_id) DO NOTHING</c> rồi đọc lại. Hai người
    /// cùng bấm "Nhắn tin" cùng lúc vẫn ra MỘT hội thoại — không 409, không 500.
    /// </summary>
    Task<(Conversation Conversation, bool Created)> GetOrCreateAsync(
        ConversationPair pair, Guid newId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Hội thoại ĐÃ CÓ TIN của <paramref name="userId"/>, sắp <c>(last_message_at DESC, id DESC)</c>, sau <paramref name="after"/>.
    /// Hai nhánh <c>UNION ALL</c> (mỗi nhánh dùng index một phần của nó), không <c>WHERE a = @me OR b = @me</c>.
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListAsync(Guid userId, ConversationCursor? after, int take, CancellationToken ct);

    /// <summary>Tổng chưa đọc của <paramref name="userId"/> trên mọi hội thoại (Đ-5.14) — MỘT câu SQL, không cache.</summary>
    Task<long> UnreadTotalAsync(Guid userId, CancellationToken ct);

    /// <summary>Tin theo lô id — tin cuối của một trang danh sách, một câu cho cả trang (luật "batch trước" Đ-2.3).</summary>
    Task<IReadOnlyDictionary<Guid, Message>> GetMessagesAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct);

    /// <summary>Tin có <c>seq &lt; beforeSeq</c> (hoặc mới nhất khi null), sắp <c>seq DESC</c> — cuộn ngược (Mục 7.6).</summary>
    Task<IReadOnlyList<Message>> HistoryAsync(Guid conversationId, long? beforeSeq, int take, CancellationToken ct);

    /// <summary>Tin có <c>seq &gt; afterSeq</c>, sắp <c>seq ASC</c> — lấp chỗ hở khi nối lại / fallback / nhảy cóc.</summary>
    Task<IReadOnlyList<Message>> AfterAsync(Guid conversationId, long afterSeq, int take, CancellationToken ct);

    /// <summary>
    /// Cập nhật mốc của MỘT thành viên (Đ-5.6) bằng một câu UPDATE: <c>GREATEST</c>, kẹp ở <c>seq_counter</c>, đã xem kéo theo
    /// đã nhận. Trả mốc mới khi mốc thật sự TĂNG, <c>null</c> khi không đổi gì (không phát <c>ReceiptUpdated</c>).
    /// </summary>
    Task<ReceiptMarks?> AdvanceMarksAsync(
        Guid conversationId, bool isUserA, ReceiptKind kind, long upToSeq, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Gửi tin — MỘT transaction theo đúng thứ tự Đ-5.4: khóa dòng hội thoại → kiểm trùng <c>client_msg_id</c> → cấp <c>seq</c>
    /// + con trỏ tin cuối + mốc của người gửi (Đ-5.14) → INSERT. Người gọi đã qua tầng 3 và BR-09 TRƯỚC khi gọi.
    /// </summary>
    Task<SendOutcome> SendAsync(SendCommand command, CancellationToken ct);
}

/// <summary>Đầu vào của <see cref="IConversationStore.SendAsync"/>. <see cref="MessageId"/> là UUID v7 sinh TRƯỚC transaction.</summary>
public sealed record SendCommand(
    Guid ConversationId, bool SenderIsUserA, Guid SenderId, Guid MessageId, string Content, Guid ClientMsgId, DateTimeOffset Now);

/// <summary>Kết quả của một lượt gửi (Đ-5.5).</summary>
public enum SendStatus
{
    /// <summary>Tin mới đã COMMIT.</summary>
    Created,

    /// <summary><c>client_msg_id</c> đã có với CÙNG nội dung — <see cref="SendOutcome.Message"/> là đúng tin cũ.</summary>
    Replayed,

    /// <summary><c>client_msg_id</c> đã dùng cho nội dung KHÁC — bug của client (409).</summary>
    ClientMsgIdReused,

    /// <summary>Hội thoại không còn tồn tại lúc khóa dòng (không xảy ra trong MVP — không xóa hội thoại).</summary>
    NotFound,
}

public sealed record SendOutcome(SendStatus Status, Message? Message);
