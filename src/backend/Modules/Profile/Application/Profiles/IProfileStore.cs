using SocialApp.Modules.Profile.Domain;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Bảng <c>profile.profiles</c> cho các luồng của khối D. Hiện thực EF nằm ở <c>Infrastructure/Persistence</c> —
/// <c>Application</c> không chạm EF (Đ-D1 của GĐ1, <c>PersistenceBoundaryTests</c> canh bằng máy).
///
/// Trả <see cref="UserProfile"/> (entity) chứ không trả <see cref="ProfileResponse"/>: <c>avatarUrl</c> là presigned GET
/// (Đ-2.9) mà store không biết R2 và không được biết. Store đọc dữ liệu, service ký URL — ranh giới đó là lý do cả hai tồn
/// tại thay vì một lớp.
///
/// Lớn dần theo từng đầu việc: <c>UpsertAsync</c> vào ở D2, <c>SetAvatarKeyAsync</c> ở D3. Khai trước một phương thức chưa
/// có người gọi là một dòng không test nào chạm tới.
/// </summary>
public interface IProfileStore
{
    /// <summary>
    /// Hồ sơ theo khóa chính, <c>null</c> khi người này chưa onboarding (Đ-2.4) HOẶC không tồn tại — store không phân biệt
    /// được hai chuyện đó và cố ý không cần: hợp đồng trả cùng một 404 cho cả hai (PROF-03).
    /// </summary>
    Task<UserProfile?> FindAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Tạo-hoặc-sửa hồ sơ trong MỘT câu lệnh và trả về dòng sau khi ghi. Phải NGUYÊN TỬ: đọc-rồi-ghi
    /// (<see cref="FindAsync"/> → null → <c>Add</c>) là hai tab cùng onboarding — cả hai thấy null, cả hai INSERT, tab
    /// sau ăn PK violation và người dùng nhận 500 ở đúng bước đầu tiên của sản phẩm.
    ///
    /// <paramref name="displayName"/> đã được service <c>Trim()</c>; <paramref name="bio"/> rỗng/toàn khoảng trắng đã
    /// được chuẩn hóa thành <c>null</c> (Q-D3). Store không tự chuẩn hóa: nó không biết luật của hợp đồng.
    ///
    /// <paramref name="now"/> đến từ <c>TimeProvider</c> của service, không phải <c>now()</c> của DB — cùng nguồn thời
    /// gian với UUID v7 và <c>created_at</c> của hai module còn lại.
    /// </summary>
    Task<UserProfile> UpsertAsync(Guid userId, string displayName, string? bio, DateTimeOffset now, CancellationToken ct);
}
