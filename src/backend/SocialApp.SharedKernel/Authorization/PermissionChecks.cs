namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Tầng 2 dưới dạng MỘT hàm (giai-doan-6.md Mục 6.2, L-C3 của hướng dẫn khối A+C): Admin short-circuit + tra cache.
/// <c>PermissionHandler</c>, handler any-of của <c>[RequireAnyPermission]</c> và tầng 2 thứ hai trong service (D7: <c>hide</c>
/// cần thêm <c>post.hide</c>) đều gọi hàm này.
///
/// Không module nào tự viết <c>role == "ADMIN"</c> (GĐ1 Mục 3.2: đặt nhầm dòng đó xuống tầng 3 là IDOR toàn hệ thống).
/// Là extension method chứ không phải thành viên của <see cref="IPermissionCache"/>: có đúng MỘT hiện thực short-circuit, không
/// fake nào trong test phải chép lại nó.
/// </summary>
public static class PermissionChecks
{
    /// <summary>
    /// <paramref name="roleCode"/> là claim <c>role</c> của token đã verify (luôn là chuỗi). Null → false. So khớp PHÂN BIỆT hoa
    /// thường: <c>"admin"</c> không phải <see cref="SystemRoles.Admin"/>. Admin → true mà KHÔNG chạm cache.
    /// </summary>
    public static async ValueTask<bool> IsAllowedAsync(
        this IPermissionCache cache, string? roleCode, string permission, CancellationToken ct = default)
    {
        if (roleCode is null)
            return false;

        if (roleCode == SystemRoles.Admin)
            return true;

        var granted = await cache.GetAsync(roleCode, ct);
        return granted.Contains(permission);
    }
}
