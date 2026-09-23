namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Đánh dấu endpoint quản trị / kiểm duyệt (giai-doan-6.md Mục 6.1 dấu ◆). MỘT attribute gánh HAI việc, để không ai gắn thiếu
/// một nửa:
/// <list type="number">
/// <item><b>Fail-closed</b> (Đ-6.8): kho thu hồi token không trả lời được (Redis chết) → 503 <see cref="RevocationUnavailableType"/>,
/// thay vì fail-open như mọi endpoint khác. Admin vừa bị hạ quyền không được giữ quyền 15 phút vì Redis hỏng.</item>
/// <item><b>Audit khi bị từ chối</b> (Đ-6.15, US-019 AC-03): tầng 2 từ chối → một dòng <c>access.denied</c>, tối đa một dòng mỗi
/// phút cho mỗi (người, route).</item>
/// </list>
/// Chỉ là metadata — KHÔNG thay <c>[RequirePermission]</c>: tầng 2 vẫn khai riêng. Gắn cho MỌI controller của <c>admin-v1</c>,
/// <c>moderation-v1</c>, trừ đúng <c>POST /reports</c> (người dùng thường báo cáo — cố ý, B.10 tự rà #8).
/// <c>Privileged_controllers_carry_the_attribute</c> canh chuyện quên gắn.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class PrivilegedEndpointAttribute : Attribute
{
    /// <summary><c>type</c> Problem Details của 503 — khai trong <c>admin-v1.yaml</c> và <c>moderation-v1.yaml</c>.</summary>
    public const string RevocationUnavailableType = "urn:socialapp:problem:revocation-unavailable";

    /// <summary>
    /// Khóa <c>HttpContext.Items</c> do <c>OnTokenValidated</c> đặt khi không kiểm được thu hồi trên endpoint đặc quyền; handler
    /// kết quả authorization đổi 401 của lần challenge đó thành 503 (L-C6 — <c>OnTokenValidated</c> chỉ Fail được thành 401).
    /// </summary>
    public const string RevocationUnavailableKey = "socialapp:revocation-unavailable";
}
