using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application;

/// <summary>
/// Mọi <see cref="Error"/> của nhóm auth ở MỘT chỗ — dễ rà PII (không thông điệp hay title nào chứa email/id, rà ở D9), và
/// AC-02 cần một đối tượng lỗi duy nhất cho hai nhánh 401. <c>ToActionResult</c> ánh xạ theo <see cref="Error.Status"/>.
/// Title ghi TƯỜNG MINH theo ví dụ trong identity-v1.yaml: hợp đồng dùng title khác nhau cho cùng 401 (login "Xác thực thất
/// bại", refresh "Phiên không hợp lệ"), nên không để rơi về title mặc định theo status.
/// </summary>
public static class IdentityErrors
{
    public static readonly Error EmailTaken = new(
        "identity.email_taken", "Email này đã được đăng ký.", 409, "Xung đột dữ liệu");
    public static readonly Error VerifyTokenInvalid = new(
        "identity.verify_invalid", "Liên kết xác minh không hợp lệ.", 400, "Dữ liệu không hợp lệ");
    public static readonly Error VerifyTokenGone = new(
        "identity.verify_gone", "Liên kết xác minh đã hết hạn hoặc đã được sử dụng.", 410, "Liên kết không còn hiệu lực");

    /// <summary>AC-02: DÙNG CHUNG cho email không tồn tại và sai mật khẩu. Không tạo lỗi thứ hai "cho rõ".</summary>
    public static readonly Error InvalidCredentials = new(
        "identity.invalid_credentials", "Email hoặc mật khẩu không đúng.", 401, "Xác thực thất bại");
    public static readonly Error EmailNotVerified = new(
        "identity.email_not_verified", "Tài khoản chưa xác minh email. Vui lòng kiểm tra hộp thư.", 403, "Bị từ chối");
    public static readonly Error Locked = new(
        "identity.locked", "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.", 423, "Tài khoản tạm khóa");

    /// <summary>MỌI nhánh hỏng của /auth/refresh (D5) — không phân biệt hết hạn/thu hồi/reuse.</summary>
    public static readonly Error SessionInvalid = new(
        "identity.session_invalid", "Phiên đăng nhập không còn hiệu lực. Vui lòng đăng nhập lại.", 401, "Phiên không hợp lệ");
}
