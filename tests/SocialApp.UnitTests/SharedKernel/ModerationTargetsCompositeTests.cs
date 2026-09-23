using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// Composite <see cref="IModerationTargets"/> (C2 GĐ6), dựng qua DI thật như host: hai provider cùng loại là lỗi CẤU HÌNH — cái nào
/// thắng sẽ tùy thứ tự <c>Add*Module</c>, nên phải ném lúc dựng, không chọn im lặng; batch trộn loại gọi mỗi provider MỘT lần.
/// </summary>
public sealed class ModerationTargetsCompositeTests
{
    [Fact]
    public void Hai_provider_cung_loai_thi_nem_luc_dung()
    {
        using var services = new ServiceCollection()
            .AddScoped<IModerationTargetProvider>(_ => new Probe(ModerationTargetType.Post))
            .AddScoped<IModerationTargetProvider>(_ => new Probe(ModerationTargetType.Post))
            .AddModerationTargets()
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IModerationTargets>());
        Assert.Contains("Post", ex.Message);
    }

    [Fact]
    public async Task Batch_tron_loai_moi_provider_mot_lan_va_id_trung_duoc_gop()
    {
        var posts = new Probe(ModerationTargetType.Post);
        var users = new Probe(ModerationTargetType.User);
        using var services = new ServiceCollection()
            .AddScoped<IModerationTargetProvider>(_ => posts)
            .AddScoped<IModerationTargetProvider>(_ => users)
            .AddModerationTargets()
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        var p = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<IModerationTargets>().GetSnapshotsAsync(
        [
            new(ModerationTargetType.Post, p), new(ModerationTargetType.Post, p), new(ModerationTargetType.Post, Guid.NewGuid()),
            new(ModerationTargetType.User, Guid.NewGuid()),
        ]);

        Assert.Equal([2], posts.BatchSizes);
        Assert.Equal([1], users.BatchSizes);
    }

    private sealed class Probe(ModerationTargetType type) : IModerationTargetProvider
    {
        public List<int> BatchSizes { get; } = [];

        public ModerationTargetType Type => type;

        public Task<IReadOnlyDictionary<Guid, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            BatchSizes.Add(ids.Count);
            return Task.FromResult<IReadOnlyDictionary<Guid, TargetSnapshot>>(new Dictionary<Guid, TargetSnapshot>());
        }

        public Task<bool> CanViewAsync(Guid actorId, Guid id, CancellationToken ct) => Task.FromResult(false);

        public Task<HideOutcome> HideAsync(DbTransaction tx, Guid id, string reasonCode, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, Guid id, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
