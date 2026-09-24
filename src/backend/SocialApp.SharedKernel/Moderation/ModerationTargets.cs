using System.Data.Common;

namespace SocialApp.SharedKernel.Moderation;

/// <summary>
/// Composite của <see cref="IModerationTargets"/> (L-C9 của hướng dẫn khối A+C GĐ6): chọn <see cref="IModerationTargetProvider"/>
/// theo loại. Sống ở SharedKernel chứ không ở host — không logic nào trong Program.cs, và host không phải biết kiểu <c>internal</c>
/// của module. Thêm loại mới (bình luận, sau khi GĐ3 merge) = MỘT dòng đăng ký provider trong module chủ.
///
/// Loại chưa có provider (bình luận trước GĐ3): ảnh chụp vắng mặt, <c>CanView</c> false (D6 → 404), ẩn/khôi phục ném
/// <see cref="NotSupportedException"/> — D7 chặn trước bằng bảng hợp lệ <c>decision × targetType</c>.
/// </summary>
internal sealed class ModerationTargets : IModerationTargets
{
    private readonly Dictionary<ModerationTargetType, IModerationTargetProvider> _providers;

    public ModerationTargets(IEnumerable<IModerationTargetProvider> providers)
    {
        _providers = [];
        foreach (var provider in providers)
        {
            // Hai provider cùng loại → cái nào thắng tùy thứ tự Add*Module trong Program.cs: ném lúc dựng, không chọn im lặng.
            if (!_providers.TryAdd(provider.Type, provider))
                throw new InvalidOperationException($"Hai IModerationTargetProvider cùng đăng ký cho loại {provider.Type}.");
        }
    }

    public async Task<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<ModerationTarget> targets, CancellationToken ct = default)
    {
        var result = new Dictionary<ModerationTarget, TargetSnapshot>();

        // Mỗi loại MỘT lời gọi batch — một trang hàng đợi trộn bài và người dùng là hai truy vấn, không phải hai mươi.
        foreach (var group in targets.GroupBy(t => t.Type))
        {
            if (!_providers.TryGetValue(group.Key, out var provider))
                continue;

            var snapshots = await provider.GetSnapshotsAsync([.. group.Select(t => t.Id).Distinct()], ct);
            foreach (var (id, snapshot) in snapshots)
                result[new ModerationTarget(group.Key, id)] = snapshot;
        }

        return result;
    }

    public Task<bool> CanViewAsync(Guid actorId, ModerationTarget target, CancellationToken ct = default) =>
        _providers.TryGetValue(target.Type, out var provider)
            ? provider.CanViewAsync(actorId, target.Id, ct)
            : Task.FromResult(false);

    public Task<HideOutcome> HideAsync(DbTransaction tx, ModerationTarget target, string reasonCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return Provider(target.Type).HideAsync(tx, target.Id, reasonCode, ct);
    }

    public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, ModerationTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return Provider(target.Type).RestoreAsync(tx, target.Id, ct);
    }

    private IModerationTargetProvider Provider(ModerationTargetType type) =>
        _providers.TryGetValue(type, out var provider)
            ? provider
            : throw new NotSupportedException($"Chưa có module nào kiểm duyệt được đối tượng loại {type}.");
}
