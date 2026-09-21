using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SocialApp.IntegrationTests.AuthZ;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure;
using SocialApp.Modules.Content.Infrastructure.Cleanup;

namespace SocialApp.IntegrationTests;

/// <summary>
/// C4 (GĐ2, Đ-2.13): worker dọn rác trên Postgres thật + Redis thật + FakeObjectStorage (C5) — không có khóa R2 trên CI
/// (Mục 10.2 mức 2). Kiểm đúng bốn thứ dễ sai của Mục 7.5: nhánh bài xóa mềm 7 ngày, nhánh mồ côi 24 giờ CHỈ dưới
/// <c>posts/</c>, không chạy khi không có khóa, và mặc định tắt (Q-C2).
///
/// Lớp này SỬA dữ liệu nên có database riêng (key <c>"media-cleanup"</c>), không đụng bản seed <c>"authz"</c> mà matrix
/// đang đọc — luật chọn hàm của <see cref="PostgresFixture"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaCleanupWorkerTests(PostgresFixture postgres, RedisFixture redis, AuthZApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<AuthZApiFactory>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public async Task InitializeAsync()
    {
        factory.UseDatabase(await postgres.SeededContentDatabaseAsync("media-cleanup"));
        _app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Redis", redis.ConnectionString);
            b.UseSetting("Media:Cleanup:Enabled", "true");
        });
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private static MediaCleanupWorker WorkerOf(WebApplicationFactory<Program> app) =>
        app.Services.GetServices<IHostedService>().OfType<MediaCleanupWorker>().Single();

    /// <summary>Bài + một ảnh, ghi thẳng qua DbContext (worker không có API nào để đi qua). Trả về storage key.</summary>
    private async Task<string> SeedPostWithMediaAsync(PostStatus status, DateTimeOffset? deletedAt)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        var post = new Post { AuthorId = Guid.NewGuid(), Body = "bài test", Status = status, DeletedAt = deletedAt, MediaCount = 1 };
        var key = $"posts/{post.AuthorId}/{Guid.NewGuid():N}.jpg";
        db.Posts.Add(post);
        db.MediaAttachments.Add(new MediaAttachment
        {
            OwnerType = MediaOwnerType.Post, OwnerId = post.PostId, StorageKey = key,
            ContentType = "image/jpeg", SizeBytes = 1000, Position = 0,
        });
        await db.SaveChangesAsync();

        factory.Storage.Put(key, 1000, "image/jpeg", lastModified: DateTimeOffset.UtcNow.AddDays(-10));
        return key;
    }

    private async Task<bool> AttachmentRowExistsAsync(string key)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        return await db.MediaAttachments.AnyAsync(m => m.StorageKey == key);
    }

    [Fact]
    public async Task Don_bai_xoa_mem_qua_7_ngay_va_mo_coi_qua_24_gio_duoi_posts_giu_moi_thu_khac()
    {
        await redis.Database.KeyDeleteAsync(MediaCleanupWorker.LockKey);
        var storage = factory.Storage;

        var deletedOld = await SeedPostWithMediaAsync(PostStatus.Deleted, DateTimeOffset.UtcNow.AddDays(-8));   // dọn
        var deletedRecent = await SeedPostWithMediaAsync(PostStatus.Deleted, DateTimeOffset.UtcNow.AddDays(-2)); // giữ: chưa đủ 7 ngày
        var live = await SeedPostWithMediaAsync(PostStatus.Published, deletedAt: null);                            // giữ

        var orphanOld = $"posts/{Guid.NewGuid()}/{Guid.NewGuid():N}.jpg";
        var orphanFresh = $"posts/{Guid.NewGuid()}/{Guid.NewGuid():N}.jpg";
        var avatarOld = $"avatars/{Guid.NewGuid()}/{Guid.NewGuid():N}.png";
        storage.Put(orphanOld, 500, "image/jpeg", DateTimeOffset.UtcNow.AddHours(-48));   // dọn
        storage.Put(orphanFresh, 500, "image/jpeg", DateTimeOffset.UtcNow.AddHours(-1));  // giữ: người dùng đang soạn bài
        storage.Put(avatarOld, 500, "image/png", DateTimeOffset.UtcNow.AddDays(-30));     // giữ: KHÔNG thuộc posts/ — avatar sống ở schema profile

        var report = await WorkerOf(_app).RunOnceAsync(CancellationToken.None);

        Assert.True(report.Ran, $"lượt bị bỏ: {report.SkipReason}");
        // Khẳng định theo KEY, không theo số đếm: các test khác trong lớp seed thêm bài xóa mềm vào cùng database "media-cleanup",
        // và thứ tự chạy của xUnit không cố định — đếm bằng 1 là đỏ ngẫu nhiên theo thứ tự.
        Assert.True(report.DeletedFromSoftDeletedPosts >= 1, "phải dọn ít nhất bài xóa mềm 8 ngày vừa seed");
        Assert.True(report.DeletedOrphans >= 1, "phải dọn ít nhất object mồ côi 48 giờ vừa seed");
        Assert.True(report.BytesReclaimed >= 1500);
        Assert.Contains(deletedOld, storage.Deleted);
        Assert.Contains(orphanOld, storage.Deleted);

        Assert.False(storage.Exists(deletedOld));
        Assert.False(await AttachmentRowExistsAsync(deletedOld));
        Assert.False(storage.Exists(orphanOld));

        Assert.True(storage.Exists(deletedRecent));
        Assert.True(await AttachmentRowExistsAsync(deletedRecent));
        Assert.True(storage.Exists(live));
        Assert.True(await AttachmentRowExistsAsync(live));
        Assert.True(storage.Exists(orphanFresh));
        Assert.True(storage.Exists(avatarOld));

        // Khóa NX EX còn đó → lượt kế ngay lập tức phải bị bỏ vì "lock", không phải chạy lần hai.
        Assert.True(await redis.Database.KeyExistsAsync(MediaCleanupWorker.LockKey));
        var second = await WorkerOf(_app).RunOnceAsync(CancellationToken.None);
        Assert.False(second.Ran);
        Assert.Equal("lock", second.SkipReason);
    }

    [Fact]
    public async Task Redis_khong_toi_duoc_thi_bo_luot_khong_xoa_gi()
    {
        // AuthZApiFactory gốc trỏ Redis vào 127.0.0.1:1 — không bao giờ kết nối được. Bật worker, có bài đủ điều kiện dọn,
        // nhưng không có khóa thì không được chạm gì (Đ-2.13: chạy không khóa là đúng thứ khóa sinh ra để ngăn).
        using var noRedis = factory.WithWebHostBuilder(b => b.UseSetting("Media:Cleanup:Enabled", "true"));
        var deletedOld = await SeedPostWithMediaAsync(PostStatus.Deleted, DateTimeOffset.UtcNow.AddDays(-8));
        var before = factory.Storage.Deleted.Count;

        var report = await WorkerOf(noRedis).RunOnceAsync(CancellationToken.None);

        Assert.False(report.Ran);
        Assert.Equal("lock", report.SkipReason);
        Assert.Equal(before, factory.Storage.Deleted.Count);
        Assert.True(factory.Storage.Exists(deletedOld));
        Assert.True(await AttachmentRowExistsAsync(deletedOld));
    }

    [Fact]
    public async Task Mac_dinh_tat_thi_bo_luot_truoc_ca_khi_xin_khoa()
    {
        // Không đặt Media:Cleanup:Enabled → mặc định false (Q-C2). Không xin khóa: Redis có hay không cũng không quan trọng.
        using var defaults = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Redis", redis.ConnectionString));
        await redis.Database.KeyDeleteAsync(MediaCleanupWorker.LockKey);

        var report = await WorkerOf(defaults).RunOnceAsync(CancellationToken.None);

        Assert.False(report.Ran);
        Assert.Equal("disabled", report.SkipReason);
        Assert.False(await redis.Database.KeyExistsAsync(MediaCleanupWorker.LockKey));
    }
}
