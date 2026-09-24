using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Moderation.DependencyInjection;
using SocialApp.Modules.Moderation.Domain;
using SocialApp.Modules.Moderation.Infrastructure;
using SocialApp.Modules.Profile.DependencyInjection;
using SocialApp.Modules.SocialGraph.DependencyInjection;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// C2 (GĐ6) — hợp đồng ghi <see cref="IModerationTargets"/> trên Postgres thật, qua composite + provider thật của Content và
/// Profile. Phần STORE của <c>HID-*</c> và bản HẠ TẦNG của <c>TX-02</c> (L-C2); bản đầy đủ qua <c>PATCH /reports</c> và
/// <c>GET /posts/{id}</c> là của D7.
///
/// Ca quan trọng nhất là <see cref="TX_02_bao_cao_an_bai_va_audit_cung_mot_transaction_loi_thi_khong_con_gi"/> — hình dạng Đ-6.13:
/// ba bảng, hai schema, MỘT transaction. Provider ghi trên kết nối riêng thì bài vẫn bị ẩn sau rollback và ca này đỏ.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ModerationTargetsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Guid Author = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Friend = Guid.Parse("01900000-0000-7000-8000-0000000000a2");
    private static readonly Guid Stranger = Guid.Parse("01900000-0000-7000-8000-0000000000a3");
    private static readonly Guid Moderator = Guid.Parse("01900000-0000-7000-8000-0000000000a4");

    private ServiceProvider _services = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _services = new ServiceCollection()
            .AddLogging()
            .AddIdentityModule(_connectionString)
            .AddProfileModule(_connectionString)
            .AddContentModule(_connectionString)
            .AddSocialGraphModule(_connectionString)
            .AddModerationModule(_connectionString)
            .AddModerationTargets()
            .BuildServiceProvider();

        await _services.MigrateIdentityModuleAsync();
        await _services.MigrateProfileModuleAsync();
        await _services.MigrateContentModuleAsync();
        await _services.MigrateSocialGraphModuleAsync();
        await _services.MigrateModerationModuleAsync();

        // Friend là bạn của Author (cặp chuẩn hóa bằng LEAST/GREATEST — đúng thứ tự uuid của Postgres, BR-03).
        await ExecuteAsync($"""
            insert into socialgraph.friendships (user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at)
            values (least('{Author}'::uuid, '{Friend}'::uuid), greatest('{Author}'::uuid, '{Friend}'::uuid), '{Author}',
                    'accepted', now(), now(), now())
            """);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var conn = new NpgsqlConnection(_connectionString);
        NpgsqlConnection.ClearPool(conn);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    private async Task<Guid> InsertPostAsync(string privacy = "public", string status = "published", Guid? author = null)
    {
        var id = Uuid7.New();
        await ExecuteAsync($"""
            insert into content.posts (post_id, author_id, body, privacy, status, deleted_at)
            values ('{id}', '{author ?? Author}', 'Nội dung bài {id}', '{privacy}', '{status}',
                    {(status == "deleted" ? "now()" : "null")})
            """);
        return id;
    }

    private static ModerationTarget Post(Guid id) => new(ModerationTargetType.Post, id);

    /// <summary>Chạy <paramref name="action"/> trong một transaction của Moderation rồi COMMIT — hình dạng D7.</summary>
    private async Task<T> InModerationTransactionAsync<T>(Func<IModerationTargets, System.Data.Common.DbTransaction, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await action(scope.ServiceProvider.GetRequiredService<IModerationTargets>(), tx.GetDbTransaction());
        await tx.CommitAsync();
        return result;
    }

    /// <summary>
    /// HID (phần store): ẩn bài published → Hidden, lý do lưu vào <c>hidden_reason</c>; lần hai → AlreadyHidden; bài đã xóa mềm và id
    /// lạ → NotFound. So sánh bằng kết quả hợp đồng, không bằng HTTP — hợp đồng không biết HTTP (Đ-6.3).
    /// </summary>
    [Fact]
    public async Task HID_store_01_an_bai_published_lan_hai_da_xoa_va_id_la()
    {
        var post = await InsertPostAsync();
        var deleted = await InsertPostAsync(status: "deleted");

        Assert.Equal(HideOutcome.Hidden, await InModerationTransactionAsync((t, tx) => t.HideAsync(tx, Post(post), ReasonCodes.Spam)));
        Assert.Equal("hidden|spam", await ScalarAsync($"select status || '|' || hidden_reason from content.posts where post_id = '{post}'"));

        Assert.Equal(HideOutcome.AlreadyHidden,
            await InModerationTransactionAsync((t, tx) => t.HideAsync(tx, Post(post), ReasonCodes.Violence)));
        Assert.Equal("spam", await ScalarAsync($"select hidden_reason from content.posts where post_id = '{post}'"));   // không ghi đè

        Assert.Equal(HideOutcome.NotFound, await InModerationTransactionAsync((t, tx) => t.HideAsync(tx, Post(deleted), ReasonCodes.Spam)));
        Assert.Equal(HideOutcome.NotFound, await InModerationTransactionAsync((t, tx) => t.HideAsync(tx, Post(Guid.NewGuid()), ReasonCodes.Spam)));
    }

    /// <summary>HID (phần store): khôi phục bài hidden → Restored, lý do bị xóa; bài đang published → NotHidden; id lạ → NotFound.</summary>
    [Fact]
    public async Task HID_store_02_khoi_phuc()
    {
        var hidden = await InsertPostAsync(status: "hidden");
        var published = await InsertPostAsync();

        Assert.Equal(RestoreOutcome.Restored, await InModerationTransactionAsync((t, tx) => t.RestoreAsync(tx, Post(hidden))));
        Assert.Equal("published|", await ScalarAsync(
            $"select status || '|' || coalesce(hidden_reason, '') from content.posts where post_id = '{hidden}'"));

        Assert.Equal(RestoreOutcome.NotHidden, await InModerationTransactionAsync((t, tx) => t.RestoreAsync(tx, Post(published))));
        Assert.Equal(RestoreOutcome.NotFound, await InModerationTransactionAsync((t, tx) => t.RestoreAsync(tx, Post(Guid.NewGuid()))));
    }

    /// <summary>
    /// TX-02 (bản hạ tầng) — hình dạng Đ-6.13: trong MỘT transaction của Moderation, ghi báo cáo (moderation.reports), ẩn bài
    /// (content.posts, qua provider của Content), ghi audit (moderation.audit_logs, qua C1), rồi lỗi trước COMMIT → không còn gì trong
    /// ba thứ đó. Đây là mốc 3 của GĐ6 ở tầng hạ tầng.
    /// </summary>
    [Fact]
    public async Task TX_02_bao_cao_an_bai_va_audit_cung_mot_transaction_loi_thi_khong_con_gi()
    {
        var post = await InsertPostAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();

            db.Reports.Add(new Report
            {
                Id = Uuid7.New(), ReporterId = Stranger, TargetType = ReportTargetTypes.Post, TargetId = post,
                ReasonCode = ReasonCodes.Spam, Status = ReportStatus.Resolved, ResolverId = Moderator,
                ResolvedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var outcome = await scope.ServiceProvider.GetRequiredService<IModerationTargets>()
                .HideAsync(tx.GetDbTransaction(), Post(post), ReasonCodes.Spam);
            Assert.Equal(HideOutcome.Hidden, outcome);

            await scope.ServiceProvider.GetRequiredService<IAuditTrail>().AppendAsync(tx.GetDbTransaction(),
                new AuditEntry(Moderator, AuditActions.ReportHide, "post", post));

            throw new InvalidOperationException("lỗi giả sau khi đã ẩn + ghi báo cáo + ghi audit, trước COMMIT");
        });

        Assert.Equal("published", await ScalarAsync($"select status from content.posts where post_id = '{post}'"));
        Assert.Equal("0", await ScalarAsync("select count(*) from moderation.reports"));
        Assert.Equal("0", await ScalarAsync("select count(*) from moderation.audit_logs"));
    }

    /// <summary>
    /// Đ-6.12 "thấy được mới báo được", qua đúng BR-02 (<c>PostVisibility.CanView</c> + <c>IFriendshipReader</c> thật): riêng tư của
    /// người khác → false; bạn bè xem bài friends → true, người lạ → false; bài đã ẩn → false với mọi người; id lạ → false.
    /// </summary>
    [Fact]
    public async Task CanView_theo_BR_02_va_chi_bai_dang_published()
    {
        var privatePost = await InsertPostAsync("private");
        var friendsPost = await InsertPostAsync("friends");
        var hiddenPost = await InsertPostAsync(status: "hidden");
        var publicPost = await InsertPostAsync();

        await using var scope = _services.CreateAsyncScope();
        var targets = scope.ServiceProvider.GetRequiredService<IModerationTargets>();

        Assert.False(await targets.CanViewAsync(Stranger, Post(privatePost)));
        Assert.True(await targets.CanViewAsync(Author, Post(privatePost)));
        Assert.True(await targets.CanViewAsync(Friend, Post(friendsPost)));
        Assert.False(await targets.CanViewAsync(Stranger, Post(friendsPost)));
        Assert.False(await targets.CanViewAsync(Stranger, Post(hiddenPost)));
        Assert.True(await targets.CanViewAsync(Stranger, Post(publicPost)));
        Assert.False(await targets.CanViewAsync(Stranger, Post(Guid.NewGuid())));
    }

    /// <summary>
    /// Ảnh chụp batch qua composite: bài (kể cả đã xóa — Moderator phải thấy) có media theo thứ tự; người dùng có trạng thái đọc qua
    /// C5 (<c>disabled</c>); bình luận chưa có provider (GĐ3) và id lạ → vắng mặt, không lỗi.
    /// </summary>
    [Fact]
    public async Task Anh_chup_batch_bai_nguoi_dung_va_loai_chua_co_provider()
    {
        var post = await InsertPostAsync("private");
        var deleted = await InsertPostAsync(status: "deleted");
        await ExecuteAsync($"""
            insert into content.media_attachments (media_id, owner_type, owner_id, storage_key, content_type, size_bytes, position)
            values (gen_random_uuid(), 'post', '{post}', 'posts/b.jpg', 'image/jpeg', 10, 1),
                   (gen_random_uuid(), 'post', '{post}', 'posts/a.jpg', 'image/jpeg', 10, 0);
            insert into profile.profiles (user_id, display_name, bio, avatar_key) values ('{Stranger}', 'Người lạ', 'Giới thiệu', 'avatars/x.jpg');
            insert into identity.users (user_id, email, password_hash, role_id, status)
            values ('{Stranger}', 'stranger@test.local', 'khong-phai-hash', 1, 'disabled');
            """);

        var comment = new ModerationTarget(ModerationTargetType.Comment, Guid.NewGuid());
        var user = new ModerationTarget(ModerationTargetType.User, Stranger);
        var missing = Post(Guid.NewGuid());

        await using var scope = _services.CreateAsyncScope();
        var snapshots = await scope.ServiceProvider.GetRequiredService<IModerationTargets>()
            .GetSnapshotsAsync([Post(post), Post(deleted), user, comment, missing]);

        Assert.Equal(3, snapshots.Count);
        Assert.Equal(("published", Author), (snapshots[Post(post)].Status, snapshots[Post(post)].AuthorId));
        Assert.Equal(["posts/a.jpg", "posts/b.jpg"], snapshots[Post(post)].MediaKeys);   // theo position, không theo thứ tự chèn
        Assert.Equal("deleted", snapshots[Post(deleted)].Status);
        Assert.Equal(("disabled", Stranger, "Giới thiệu"), (snapshots[user].Status, snapshots[user].AuthorId, snapshots[user].Body));
        Assert.Equal(["avatars/x.jpg"], snapshots[user].MediaKeys);
    }

    /// <summary>Người dùng không ẩn được; bình luận chưa có provider — ném, không lặng lẽ trả NotFound (D7 chặn trước bằng bảng hợp lệ).</summary>
    [Fact]
    public async Task An_nguoi_dung_hoac_loai_chua_co_provider_thi_nem_NotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => InModerationTransactionAsync(
            (t, tx) => t.HideAsync(tx, new ModerationTarget(ModerationTargetType.User, Stranger), ReasonCodes.Spam)));
        await Assert.ThrowsAsync<NotSupportedException>(() => InModerationTransactionAsync(
            (t, tx) => t.HideAsync(tx, new ModerationTarget(ModerationTargetType.Comment, Guid.NewGuid()), ReasonCodes.Spam)));

        await using var scope = _services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IModerationTargets>()
            .CanViewAsync(Stranger, new ModerationTarget(ModerationTargetType.Comment, Guid.NewGuid())));
    }
}
