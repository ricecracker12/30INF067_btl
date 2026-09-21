using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Redis;
using SocialApp.SharedKernel.Storage;
using StackExchange.Redis;

namespace SocialApp.Modules.Content.Infrastructure.Cleanup;

/// <summary>
/// Worker dọn rác media (Đ-2.13, Mục 7.5) — hai việc, mỗi chu kỳ một lần, CHỈ trên instance lấy được khóa Redis:
/// <list type="number">
/// <item><b>Bài xóa mềm quá 7 ngày</b>: xóa object rồi xóa dòng <c>media_attachments</c> (Đ-2.10 — <c>DELETE /posts</c> không
/// xóa object trong request: bấm nhầm là mất ảnh vĩnh viễn, và commit rollback sau khi xóa object là bài còn mà ảnh mất).</item>
/// <item><b>Object mồ côi quá 24 giờ</b> dưới tiền tố <c>posts/</c>: có trong bucket mà không có dòng <c>media_attachments</c>
/// (người dùng PUT xong rồi bỏ đi — SEQ-01 chỗ hỏng thứ 3).</item>
/// </list>
///
/// Vì sao chỉ quét <c>posts/</c> chứ không cả bucket: avatar KHÔNG có dòng <c>media_attachments</c> — nó sống ở
/// <c>profile.profiles.avatar_key</c>, schema mà module Content không được đọc (Đ-2.2, Đ-2.3). Quét cả bucket là xóa nhầm
/// avatar đang dùng. Avatar mồ côi (đổi avatar để lại object cũ, <c>DELETE /users/me/avatar</c> chỉ gỡ liên kết) là việc
/// của module Profile, hoãn có địa chỉ — ghi ở giai-doan-2.md Mục 7.5.
///
/// Khóa: <c>SET lock:media-cleanup &lt;instance&gt; NX EX 3000</c> trên kết nối Redis chung của SharedKernel. GĐ7 chạy 2
/// container api — không khóa thì hai worker cùng quét cùng xóa, và đó là lỗi chỉ xuất hiện trên production. Viết ngay ở
/// GĐ2 vì lúc GĐ7 nó không tái hiện được ở dev một container. Redis không kết nối được → BỎ lượt, KHÔNG chạy khi không có
/// khóa: chạy không khóa là đúng thứ khóa sinh ra để ngăn. Không nhả khóa sau lượt — EX 3000 s tự nhả trước chu kỳ kế,
/// và một instance chết giữa lượt cũng không kẹt.
///
/// Đây là chỗ DUY NHẤT trong module được <c>IgnoreQueryFilters()</c> (Đ-2.10, A5): global query filter loại bài xóa mềm
/// khỏi mọi truy vấn đọc, mà việc của worker là tìm đúng những bài đó. Thấy <c>IgnoreQueryFilters</c> ở D6/D8 là code
/// review phải chặn.
///
/// Không bao giờ log key của người dùng hay URL đã ký — chỉ log số object và số byte.
/// </summary>
public sealed class MediaCleanupWorker(
    IServiceScopeFactory scopes,
    IObjectStorage storage,
    RedisConnection redis,
    IOptions<MediaCleanupOptions> options,
    ILogger<MediaCleanupWorker> logger) : BackgroundService
{
    public const string LockKey = "lock:media-cleanup";

    /// <summary>Chỉ quét tiền tố của ảnh bài. Xem phần đầu lớp — không mở rộng sang <c>avatars/</c> từ module này.</summary>
    public const string OrphanPrefix = "posts/";

    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Worker dọn rác media đang TẮT ({Section}:Enabled=false) — không quét", MediaCleanupOptions.Section);
            return;
        }

        // Lượt đầu SAU một chu kỳ, không chạy ngay lúc khởi động: deploy hai instance cùng lúc thì cả hai cùng xin khóa đúng
        // lúc app còn đang warm-up, và test dựng host với Enabled=true không bị lượt quét bất ngờ chen vào.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(MediaCleanupOptions.IntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Một lượt hỏng không được giết worker: lượt sau thử lại. Khóa tự hết hạn.
                    logger.LogError(ex, "Lượt dọn rác media thất bại; sẽ thử lại ở chu kỳ kế");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host dừng — thoát êm.
        }
    }

    /// <summary>
    /// Một lượt quét. Public để test gọi thẳng thay vì chờ chu kỳ 60 phút; kết quả nói rõ lượt có chạy không và vì sao bỏ.
    /// </summary>
    public async Task<MediaCleanupReport> RunOnceAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
            return MediaCleanupReport.Skipped("disabled");

        if (!await TryAcquireLockAsync(ct))
            return MediaCleanupReport.Skipped("lock");

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var (fromDeletedPosts, bytes1) = await CleanSoftDeletedPostsAsync(db, now, ct);
        var (orphans, bytes2) = await CleanOrphansAsync(db, now, ct);

        var report = new MediaCleanupReport(true, null, fromDeletedPosts, orphans, bytes1 + bytes2);
        logger.LogInformation(
            "Dọn rác media: {FromDeletedPosts} object của bài xóa mềm, {Orphans} object mồ côi, thu hồi {Bytes} byte",
            report.DeletedFromSoftDeletedPosts, report.DeletedOrphans, report.BytesReclaimed);
        return report;
    }

    private async Task<bool> TryAcquireLockAsync(CancellationToken ct)
    {
        // GetAsync (chờ lần kết nối đầu) chứ không ConnectedOrNull (không chờ): worker nền chờ được vài giây, còn
        // ConnectedOrNull đúng lúc app vừa khởi động trả null giả và lượt bị bỏ oan. Redis chết thì task vẫn xong với
        // multiplexer chưa kết nối (AbortOnConnectFail=false) — kiểm IsConnected rồi bỏ lượt, không ném ra ngoài.
        ConnectionMultiplexer mux;
        try
        {
            mux = await redis.GetAsync().WaitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Không kết nối được Redis — bỏ lượt dọn rác media (không chạy khi không có khóa)");
            return false;
        }

        if (!mux.IsConnected)
        {
            logger.LogWarning("Redis chưa kết nối — bỏ lượt dọn rác media (không chạy khi không có khóa)");
            return false;
        }

        try
        {
            // NX: chỉ instance đầu tiên đặt được; EX: tự nhả trước chu kỳ kế (LockSeconds < IntervalMinutes*60).
            return await mux.GetDatabase().StringSetAsync(
                LockKey, _instanceId, TimeSpan.FromSeconds(MediaCleanupOptions.LockSeconds), When.NotExists);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Lệnh SET NX thất bại — bỏ lượt dọn rác media");
            return false;
        }
    }

    /// <summary>Nhánh (2): object của bài xóa mềm quá <see cref="MediaCleanupOptions.DeletedPostAgeDays"/> ngày.</summary>
    private async Task<(int Count, long Bytes)> CleanSoftDeletedPostsAsync(ContentDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-MediaCleanupOptions.DeletedPostAgeDays);

        // CHỖ DUY NHẤT được IgnoreQueryFilters trong module (Đ-2.10, A5) — xem phần đầu lớp.
        var postIds = await db.Posts.IgnoreQueryFilters()
            .Where(p => p.Status == PostStatus.Deleted && p.DeletedAt != null && p.DeletedAt < cutoff)
            .OrderBy(p => p.DeletedAt)
            .Select(p => p.PostId)
            .Take(MediaCleanupOptions.BatchSize)
            .ToListAsync(ct);

        if (postIds.Count == 0)
            return (0, 0);

        var attachments = await db.MediaAttachments
            .Where(m => m.OwnerType == MediaOwnerType.Post && postIds.Contains(m.OwnerId))
            .ToListAsync(ct);

        var count = 0;
        long bytes = 0;
        foreach (var attachment in attachments)
        {
            // Object TRƯỚC, dòng SAU, lưu từng cặp: hỏng giữa chừng thì còn dòng trỏ tới object đã mất (nhánh (1) không
            // thấy, nhưng vô hại) chứ không phải object mồ côi không dấu vết. Ngược lại thì phải chờ nhánh (1) quét cả
            // bucket mới dọn được.
            await storage.DeleteAsync(attachment.StorageKey, ct);
            db.MediaAttachments.Remove(attachment);
            await db.SaveChangesAsync(ct);
            count++;
            bytes += attachment.SizeBytes;
        }

        return (count, bytes);
    }

    /// <summary>Nhánh (1): object dưới <see cref="OrphanPrefix"/> cũ hơn <see cref="MediaCleanupOptions.OrphanAgeHours"/> giờ, không có dòng.</summary>
    private async Task<(int Count, long Bytes)> CleanOrphansAsync(ContentDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-MediaCleanupOptions.OrphanAgeHours);

        // Một trang mỗi lượt (BatchSize): bucket lớn không giữ khóa suốt cả tiếng; mồ côi đã xóa nên lượt sau tự tiến.
        var page = await storage.ListAsync(OrphanPrefix, continuationToken: null, MediaCleanupOptions.BatchSize, ct);
        var candidates = page.Items.Where(i => i.LastModified < cutoff).ToList();
        if (candidates.Count == 0)
            return (0, 0);

        // Đối chiếu với media_attachments NGAY TRONG LƯỢT, không dùng danh sách đã tải trước: bài vừa đăng xong giữa hai
        // bước là ảnh thật, xóa nhầm là mất ảnh của người dùng.
        var keys = candidates.Select(c => c.Key).ToList();
        var referenced = (await db.MediaAttachments
                .Where(m => keys.Contains(m.StorageKey))
                .Select(m => m.StorageKey)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var count = 0;
        long bytes = 0;
        foreach (var orphan in candidates.Where(c => !referenced.Contains(c.Key)))
        {
            await storage.DeleteAsync(orphan.Key, ct);
            count++;
            bytes += orphan.Size;
        }

        return (count, bytes);
    }
}

/// <summary>Kết quả một lượt: <see cref="Ran"/> false kèm <see cref="SkipReason"/> (<c>disabled</c> | <c>lock</c>).</summary>
public sealed record MediaCleanupReport(bool Ran, string? SkipReason, int DeletedFromSoftDeletedPosts, int DeletedOrphans, long BytesReclaimed)
{
    public static MediaCleanupReport Skipped(string reason) => new(false, reason, 0, 0, 0);
}
