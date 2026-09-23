using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Profile.Infrastructure.Moderation;

/// <summary>
/// Provider NGƯỜI DÙNG của <see cref="IModerationTargets"/> (C2 GĐ6). Chỉ ĐỌC: người dùng không "ẩn" được — báo cáo người dùng đóng
/// bằng <c>resolve</c> kèm ghi chú (Moderator không có <c>user.lock</c>, Đ-6.13), khóa tài khoản là việc của Admin ở Identity.
///
/// Ảnh chụp: tiểu sử làm <see cref="TargetSnapshot.Body"/>, ảnh đại diện làm media (tên hiển thị D7 hydrate qua <c>IUserDirectory</c>
/// như mọi <c>author</c>); trạng thái <c>active|disabled</c> đọc qua <see cref="IAccountStatusReader"/> (C5) — Profile không có cột
/// <c>status</c>, và không đọc bảng của Identity (lệch thứ tự B.5: C5 trước C2, L-C8).
/// </summary>
internal sealed class ProfileModerationTargets(ProfileDbContext db, IAccountStatusReader accounts) : IModerationTargetProvider
{
    public ModerationTargetType Type => ModerationTargetType.User;

    public async Task<IReadOnlyDictionary<Guid, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, TargetSnapshot>();

        var userIds = ids.ToArray();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Bio, p.AvatarKey, p.CreatedAt })
            .ToListAsync(ct);

        var inactive = await accounts.GetInactiveAsync([.. profiles.Select(p => p.UserId)], ct);

        return profiles.ToDictionary(
            p => p.UserId,
            p => new TargetSnapshot(
                new ModerationTarget(ModerationTargetType.User, p.UserId),
                inactive.Contains(p.UserId) ? "disabled" : "active",
                p.UserId,
                p.Bio,
                p.AvatarKey is null ? [] : [p.AvatarKey],
                null,
                p.CreatedAt,
                null));
    }

    /// <summary>Đ-6.12, đối tượng người dùng: "thấy được" = có hồ sơ (hồ sơ là công khai với người đã đăng nhập — GĐ2).</summary>
    public Task<bool> CanViewAsync(Guid actorId, Guid id, CancellationToken ct) =>
        db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == id, ct);

    public Task<HideOutcome> HideAsync(DbTransaction tx, Guid id, string reasonCode, CancellationToken ct) =>
        throw new NotSupportedException("Người dùng không ẩn được — báo cáo người dùng đóng bằng resolve (Đ-6.13).");

    public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, Guid id, CancellationToken ct) =>
        throw new NotSupportedException("Người dùng không ẩn được nên không có gì để khôi phục (Đ-6.13).");
}
