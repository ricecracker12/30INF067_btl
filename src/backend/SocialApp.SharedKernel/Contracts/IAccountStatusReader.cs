namespace SocialApp.SharedKernel.Contracts;

/// <summary>
/// Cửa để một module biết tài khoản nào KHÔNG hoạt động mà không đọc bảng <c>identity.users</c> (Đ-6.19 của GĐ6). Người dùng:
/// tìm kiếm (Profile, D12 — không hiện tài khoản bị khóa), ảnh chụp đối tượng người dùng cho Moderator (Profile, C2), và GĐ8 (mọi
/// danh sách công khai phải lọc tài khoản đã xóa).
///
/// Ba luật Đ-2.3, như <see cref="IUserDirectory"/>: <b>chỉ đọc</b> · <b>chỉ chiếu</b> (trả tập id, không entity) · <b>batch, không có
/// bản đơn</b> — ô tìm kiếm gọi hàm này cho 20 ứng viên mỗi lần gõ phím, một <c>IsActiveAsync(Guid)</c> "cho tiện" là N+1 đúng chỗ
/// người dùng đang chờ.
/// </summary>
public interface IAccountStatusReader
{
    /// <summary>
    /// Tập con của <paramref name="userIds"/> có <c>status &lt;&gt; 'active'</c> — <c>disabled</c> (Admin khóa, Đ-6.5), <c>deleted</c>
    /// (GĐ8), và <c>locked</c> (giá trị không ai ghi, giữ trong CHECK). MỘT truy vấn cho cả lô; danh sách rỗng → tập rỗng, không chạm
    /// DB. Id KHÔNG tồn tại thì KHÔNG có trong kết quả — người gọi không được suy "không tồn tại" từ hàm này.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetInactiveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
