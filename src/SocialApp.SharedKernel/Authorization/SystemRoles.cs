namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Vai trò mà tầng 2 short-circuit (Mục 3.2). Chỉ ADMIN ở đây: SharedKernel cần biết vai trò nào được đi
/// lối tắt, không cần biết danh sách vai trò — danh sách thuộc Identity (RoleCodes.Admin trỏ về đây, Đ1).
///
/// Chuỗi "ADMIN" được gõ ĐÚNG MỘT LẦN trong repo tại đây, nên kiểm tra vai trò hệ thống lúc khởi động (A5)
/// và short-circuit không thể lệch nhau. Đổi giá trị là phá hợp đồng với mọi token đang lưu hành.
/// </summary>
public static class SystemRoles
{
    public const string Admin = "ADMIN";
}
