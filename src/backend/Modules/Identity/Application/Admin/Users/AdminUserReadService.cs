using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// <c>GET /admin/users</c>, <c>GET /admin/users/{userId}</c> (GĐ6 D2, UC-20). Tầng 2 (any-of <c>user.lock</c> / <c>user.unlock</c> /
/// <c>role.assign</c>) và fail-closed đã xong ở controller; không có tầng 3 — endpoint đặc quyền, người ngoài đã bị chặn trước đó,
/// nên 404 cho id không tồn tại không lộ gì (hướng dẫn khối D, D2 bước 4).
///
/// Số câu SQL mỗi request KHÔNG phụ thuộc số dòng (ADM-07c): một câu <c>users ⋈ roles</c> + một lô <c>IUserDirectory</c>.
/// </summary>
public sealed class AdminUserReadService(IAdminUserQueries queries, IUserDirectory directory, TimeProvider clock)
{
    /// <summary>Gọi SAU validator: cursor đã giải mã được, limit trong <c>1..50</c>, status/roleCode đúng dạng.</summary>
    public async Task<AdminUserPage> ListAsync(ListAdminUsersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        AdminUserCursor? after = AdminUserCursor.TryDecode(query.Cursor, out var decoded) ? decoded : null;
        var limit = query.EffectiveLimit;
        var rows = await queries.ListAsync(
            new AdminUserFilter(query.Q, query.Status, query.RoleCode), after, limit + 1, ct);

        var page = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        var next = rows.Count > limit ? new AdminUserCursor(page[^1].CreatedAt, page[^1].UserId).Encode() : null;
        return new AdminUserPage(await HydrateAsync(page, ct), next);
    }

    /// <summary>Không tồn tại → 404.</summary>
    public async Task<Result<AdminUser>> GetAsync(Guid userId, CancellationToken ct)
    {
        var row = await queries.FindAsync(userId, ct);
        if (row is null)
            return IdentityErrors.UserNotFound;

        return (await HydrateAsync([row], ct))[0];
    }

    /// <summary>
    /// MỘT lô Profile cho cả trang — hydrate trong vòng lặp là N+1 đúng ở màn Admin cuộn nhiều (cạm bẫy 2 của D2). Người chưa có
    /// hồ sơ thì vắng mặt trong dictionary → <c>displayName = null</c>.
    /// </summary>
    private async Task<IReadOnlyList<AdminUser>> HydrateAsync(IReadOnlyList<AdminUserRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var cards = await directory.GetManyAsync([.. rows.Select(r => r.UserId).Distinct()], ct);
        var now = clock.GetUtcNow();

        return [.. rows.Select(r => new AdminUser(
            r.UserId,
            r.Email,
            cards.TryGetValue(r.UserId, out var card) ? card.DisplayName : null,
            r.RoleCode,
            r.RoleDisplayName,
            r.Status,
            r.EmailVerifiedAt is not null,
            // Khóa tạm FR-003 đã qua mốc là tài khoản đã tự mở — cột chỉ được dọn ở lần đăng nhập đúng kế tiếp.
            r.LockedUntil > now ? r.LockedUntil : null,
            r.CreatedAt))];
    }
}
