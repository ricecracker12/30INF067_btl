namespace SocialApp.SharedKernel.Contracts;

/// <summary>
/// Chiếu (projection) của hồ sơ — <b>KHÔNG phải entity</b> (Đ-2.3 luật 2). Module Content nhận kiểu này,
/// không bao giờ nhận <c>UserProfile</c>: đưa entity ra ngoài là mở đường cho module khác ghi vào bảng của
/// module chủ, và là thứ ranh giới schema của Đ-2.1 sinh ra để chặn.
///
/// Mang <c>AvatarKey</c> (khóa của object trên R2), <b>không</b> mang URL: ký presigned GET là việc của khối
/// D, và SharedKernel ở tầng này không biết gì về R2.
/// </summary>
public sealed record UserCard(Guid UserId, string DisplayName, string? AvatarKey);

/// <summary>
/// Cửa duy nhất để một module đọc hồ sơ của module Profile mà KHÔNG import module đó (Đ-2.3). Contract nằm
/// ở SharedKernel chứ không ở module chủ vì <c>ModuleBoundaryTests</c> chặn mọi phụ thuộc giữa hai namespace
/// <c>SocialApp.Modules.*</c> — kể cả phụ thuộc vào một interface.
///
/// Ba luật của Đ-2.3, áp từ đây để SharedKernel không thành cái sọt:
/// <list type="number">
/// <item><b>Chỉ đọc</b> — không phương thức ghi. Module muốn module khác ghi hộ là dấu hiệu chia module sai.</item>
/// <item><b>Chỉ chiếu</b> — trả <see cref="UserCard"/>, không trả entity.</item>
/// <item><b>Batch, không có bản đơn.</b></item>
/// </list>
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Đọc nhiều hồ sơ trong MỘT truy vấn. GĐ4 gọi hàm này cho 20 bài mỗi trang feed — thêm một
    /// <c>GetAsync(Guid)</c> "cho tiện" là mở lại đường N+1 đúng ở endpoint trọng điểm hiệu năng (GOAL-01),
    /// và không ai thấy cho tới lúc chạy k6.
    /// </summary>
    /// <returns>
    /// Id nào không có hồ sơ thì <b>vắng mặt</b> trong dictionary — không phải giá trị <c>null</c>. Người gọi
    /// phải xử lý trường hợp vắng mặt, dù Đ-2.4 (chưa có hồ sơ thì không đăng được bài) làm cho nó gần như
    /// không xảy ra.
    /// </returns>
    Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
