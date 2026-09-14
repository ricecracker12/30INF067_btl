using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application;

/// <summary>
/// Mọi <see cref="Error"/> của nhóm auth ở MỘT chỗ — dễ rà PII ở D9 (không thông điệp nào chứa email/id), và
/// AC-02 cần một đối tượng lỗi duy nhất cho hai nhánh 401. <c>ToActionResult</c> ánh xạ theo <see cref="Error.Status"/>.
/// </summary>
public static class IdentityErrors
{
    public static readonly Error EmailTaken = new("identity.email_taken", "Email này đã được đăng ký.", 409);
    public static readonly Error VerifyTokenInvalid = new("identity.verify_invalid", "Liên kết xác minh không hợp lệ.", 400);
    public static readonly Error VerifyTokenGone = new("identity.verify_gone", "Liên kết xác minh đã hết hạn hoặc đã được sử dụng.", 410);

    /// <summary>AC-02: DÙNG CHUNG cho email không tồn tại và sai mật khẩu. Không tạo lỗi thứ hai "cho rõ".</summary>
    public static readonly Error InvalidCredentials = new("identity.invalid_credentials", "Email hoặc mật khẩu không đúng.", 401);
    public static readonly Error EmailNotVerified = new("identity.email_not_verified", "Tài khoản chưa xác minh email. Vui lòng kiểm tra hộp thư.", 403);
    public static readonly Error Locked = new("identity.locked", "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.", 423);

    /// <summary>MỌI nhánh hỏng của /auth/refresh (D5) — không phân biệt hết hạn/thu hồi/reuse.</summary>
    public static readonly Error SessionInvalid = new("identity.session_invalid", "Phiên đăng nhập không còn hiệu lực. Vui lòng đăng nhập lại.", 401);
}
