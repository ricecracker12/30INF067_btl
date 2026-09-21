using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Profile.Application;

/// <summary>
/// Mọi <see cref="Error"/> của module Profile ở MỘT chỗ — cùng lý do với <c>IdentityErrors</c> của GĐ1: rà PII bằng một lần
/// đọc ở D9 (không thông điệp nào chứa id, key, email hay tên kiểu .NET), và một loại lỗi chỉ có đúng một cách nói.
///
/// Thông điệp chép ĐÚNG câu trong <c>example</c> của <c>profile-v1.yaml</c>. Luật frontend Mục 6 bắt FE hiện câu của server;
/// sửa câu ở đây mà không sửa yaml là hai nguồn lệch nhau.
///
/// Không có lỗi 403 nào trong danh sách này: 403 của Profile (avatar mang tiền tố của người khác — Đ-2.7; người gọi chưa có
/// hồ sơ — Q-D9) dùng <see cref="Error.Forbidden"/> của SharedKernel. Tạo bản thứ hai là mở đường cho hai câu chữ khác nhau
/// cho cùng một loại từ chối, tức là lộ ra lý do bị từ chối.
/// </summary>
public static class ProfileErrors
{
    /// <summary>
    /// 404 của <c>GET /users/{userId}/profile</c> — và là TÍN HIỆU ONBOARDING của FE (Đ-2.4, Mục 7.1): "chưa có hồ sơ" và
    /// "người dùng không tồn tại" cố ý cùng một phản hồi, nên <c>detail</c> KHÔNG được nêu <c>userId</c> (PROF-03).
    /// </summary>
    public static readonly Error NotFound = new("profile.not_found", "Người dùng này chưa có hồ sơ.", 404);

    /// <summary>
    /// Đ-2.8 lớp 2 cho avatar (D3): HEAD lên R2 không thấy object. Chỉ biết được SAU I/O nên không phải việc của validator —
    /// xem <see cref="Error.Validation"/>. Property chứ không <c>static readonly</c> field: mỗi lần dùng là một dictionary
    /// mới, không ai vô tình giữ tham chiếu chung.
    /// </summary>
    public static Error AvatarNotUploaded =>
        Error.Validation("mediaKey", "Ảnh chưa được tải lên xong. Hãy chờ tải lên hoàn tất rồi thử lại.");

    /// <summary>Đ-2.8 lớp 2 cho avatar (D3): object có thật nhưng <c>Content-Type</c> ngoài allowlist.</summary>
    public static Error AvatarTypeNotAllowed =>
        Error.Validation("mediaKey", "Ảnh đại diện chỉ nhận JPEG, PNG hoặc WebP.");
}
