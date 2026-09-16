using System.Security.Claims;

namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// Danh tính người gọi lấy từ token đã verify — NGUỒN DUY NHẤT cho kiểm tra ownership ở tầng 3. Nhận actorId
/// từ route hay body "vì client đã gửi sẵn" chính là IDOR.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtClaims.Sub), out var id)
            ? id
            : throw new InvalidOperationException("Principal không có claim sub hợp lệ — endpoint này thiếu tầng 1?");
}
