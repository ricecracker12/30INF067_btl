using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.DependencyInjection;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Bản Content của <see cref="IdentityDbContextSchemaTests"/> (A5, GĐ2 khối A) — và là test đắt nhất của
/// khối A: sáu khẳng định, mỗi cái ứng với một thứ mà NẾU cấu hình sai thì không có lỗi nào khác báo.
/// Migration vẫn chạy, app vẫn lên, chỉ có bất biến biến mất.
///
/// Mỗi test một database mới chưa migrate, trên cùng <see cref="PostgresFixture"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentDbContextSchemaTests(PostgresFixture postgres)
{
    private static async Task<(ServiceProvider Services, string ConnectionString)> MigratedAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection()
            .AddContentModule(connectionString)
            .BuildServiceProvider();

        await services.MigrateContentModuleAsync();
        return (services, connectionString);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>Khẳng định 1 — bảng lịch sử của Content nằm trong schema <c>content</c>, không ở <c>public</c>.</summary>
    [Fact]
    public async Task Migrate_dat_bang_lich_su_vao_schema_content()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        var schemas = await db.Database
            .SqlQuery<string>($"""
                select table_schema as "Value"
                from information_schema.tables
                where table_name = '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal([ContentDbContext.Schema], schemas);
    }

    /// <summary>Đủ bốn bảng của Mục 4, tất cả trong schema <c>content</c>.</summary>
    [Fact]
    public async Task Migrate_tao_du_bon_bang_cua_Muc_4()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        var tables = await db.Database
            .SqlQuery<string>($"""
                select table_name as "Value"
                from information_schema.tables
                where table_schema = 'content' and table_name <> '__EFMigrationsHistory'
                """)
            .ToListAsync();

        Assert.Equal(["comments", "media_attachments", "posts", "reactions"], tables.Order());
    }

    /// <summary>
    /// Khẳng định 2 — BR-01 ở tầng DB: bài không chữ, không ảnh bị <c>ck_posts_not_empty</c> chặn. Đi bằng
    /// SQL thô vì đây là lưới cho chính những đường KHÔNG qua validator của D5.
    /// </summary>
    [Fact]
    public async Task Bai_khong_chu_khong_anh_bi_chan()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);
        await using var cmd = new NpgsqlCommand(
            """
            insert into content.posts (post_id, author_id, body, media_count, created_at, updated_at)
            values (gen_random_uuid(), gen_random_uuid(), null, 0, now(), now())
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_posts_not_empty", ex.ConstraintName);
    }

    /// <summary>
    /// Khẳng định 3 — mặt CÒN LẠI của khẳng định 2: bài chỉ có ảnh (BR01-06) phải VÀO ĐƯỢC. Thiếu test này
    /// thì một CHECK viết quá chặt (ví dụ đòi body luôn khác rỗng) vẫn xanh, và cả luồng UC-04 chết ở khối D.
    /// </summary>
    [Fact]
    public async Task Bai_chi_co_anh_thi_tao_duoc()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);
        await using var cmd = new NpgsqlCommand(
            """
            insert into content.posts (post_id, author_id, body, media_count, created_at, updated_at)
            values (gen_random_uuid(), gen_random_uuid(), null, 1, now(), now())
            """, conn);

        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// Khẳng định 4 — <c>storage_key</c> UNIQUE chặn gắn CÙNG một object vào hai bài. Đây là nguồn của 409
    /// ở D5 (POST-08): D5 bắt đúng vi phạm này và dịch thành 409, không để rơi thành 500.
    /// </summary>
    [Fact]
    public async Task Hai_anh_cung_storage_key_bi_chan()
    {
        var (_, connectionString) = await MigratedAsync(postgres);

        await using var conn = await OpenAsync(connectionString);

        async Task<int> InsertAsync(short position)
        {
            await using var cmd = new NpgsqlCommand(
                """
                insert into content.media_attachments
                    (media_id, owner_type, owner_id, storage_key, content_type, size_bytes, position, created_at)
                values
                    (gen_random_uuid(), 'post', gen_random_uuid(), 'posts/ai-do/mot-object.jpg', 'image/jpeg', 1024, @p, now())
                """, conn);
            cmd.Parameters.AddWithValue("p", position);
            return await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(1, await InsertAsync(0));

        // Bài khác (owner_id khác) và vị trí khác — chỉ storage_key là trùng, để chắc chắn thứ chặn là
        // UNIQUE của storage_key chứ không phải uq_media_owner_position.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(1));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("IX_media_attachments_storage_key", ex.ConstraintName);
    }

    /// <summary>
    /// Khẳng định 5 — <c>reaction_counts</c> của bài mới đọc ra <c>{}</c>, KHÔNG <c>null</c> (Mục 8.2).
    /// Chốt Bước 4 của A4 bằng hành vi thật: FE viết một lần, GĐ3 không phải sửa.
    /// </summary>
    [Fact]
    public async Task Reaction_counts_cua_bai_moi_doc_ra_rong_chu_khong_null()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        db.Posts.Add(new Post { AuthorId = Guid.NewGuid(), Body = "bài đầu tiên" });
        await db.SaveChangesAsync();

        // Đọc lại từ DB chứ không đọc entity đang được theo dõi.
        db.ChangeTracker.Clear();
        var post = await db.Posts.SingleAsync();

        Assert.NotNull(post.ReactionCounts);
        Assert.Empty(post.ReactionCounts);
    }

    /// <summary>
    /// Khẳng định 6 — global query filter của Đ-2.10 chạy thật: bài <c>status='deleted'</c> không xuất hiện
    /// qua <c>db.Posts</c>.
    ///
    /// Dòng "deleted" tạo bằng SQL thô để đi VÒNG qua ChangeTracker. Tạo bằng DbSet rồi đọc bằng DbSet thì
    /// filter tự loại ở cả hai chiều và test không chứng minh được gì.
    /// </summary>
    [Fact]
    public async Task Bai_xoa_mem_khong_xuat_hien_qua_DbSet()
    {
        var (services, connectionString) = await MigratedAsync(postgres);

        await using (var conn = await OpenAsync(connectionString))
        {
            await using var cmd = new NpgsqlCommand(
                """
                insert into content.posts (post_id, author_id, body, status, media_count, created_at, updated_at, deleted_at)
                values (gen_random_uuid(), gen_random_uuid(), 'đã xóa', 'deleted', 0, now(), now(), now()),
                       (gen_random_uuid(), gen_random_uuid(), 'còn sống', 'published', 0, now(), now(), null)
                """, conn);
            Assert.Equal(2, await cmd.ExecuteNonQueryAsync());
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        var bodies = await db.Posts.Select(p => p.Body).ToListAsync();
        Assert.Equal(["còn sống"], bodies);

        // Và đường của worker dọn rác (C4) vẫn thấy cả hai — nếu không thì filter đang chặn cả chỗ được
        // phép đi vòng, và C4 sẽ không bao giờ xóa được object nào trên R2.
        var all = await db.Posts.IgnoreQueryFilters().CountAsync();
        Assert.Equal(2, all);
    }

    /// <summary>
    /// A4 (GĐ4) — index feed gợi ý: keyset <c>(created_at DESC, post_id DESC)</c> trên bài
    /// <c>published</c> + <c>public</c>. Filter sai thì truy vấn vẫn đúng kết quả nhưng không dùng index
    /// — chỉ lộ ở EXPLAIN của C2; test này bắt sớm hơn.
    /// </summary>
    [Fact]
    public async Task Index_public_recent_dung_dinh_nghia_keyset_va_filter()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        var indexDef = await db.Database
            .SqlQuery<string>($"""
                select indexdef as "Value"
                from pg_indexes
                where schemaname = 'content' and indexname = 'idx_posts_public_recent'
                """)
            .SingleAsync();

        // Khẳng định NGUYÊN mệnh đề WHERE như pg_indexes chuẩn hóa ra — kiểm từng từ rời thì filter đảo cột
        // (privacy = 'published' AND status = 'public') vẫn lọt.
        Assert.Contains("(created_at DESC, post_id DESC)", indexDef);
        Assert.EndsWith(
            "WHERE (((status)::text = 'published'::text) AND ((privacy)::text = 'public'::text))", indexDef);
    }

    /// <summary>
    /// A3 (GĐ3, Mục 4 cạm bẫy 1) — bộ index của <c>comments</c> sau <c>Gd3Interactions</c>: hai index keyset mới có mặt,
    /// index quy ước của FK <c>parent_id</c> đã được thay, và <c>IX_comments_post_id</c> (FK <c>post_id</c> ON DELETE
    /// CASCADE) CÒN — index một phần của trang gốc không thay được nó.
    /// </summary>
    [Fact]
    public async Task Index_comments_GD3_dung_bo_va_con_IX_comments_post_id()
    {
        var (services, _) = await MigratedAsync(postgres);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();

        var indexes = (await db.Database
                .SqlQuery<string>($"""
                    select indexname || ' ' || indexdef as "Value"
                    from pg_indexes
                    where schemaname = 'content' and tablename = 'comments'
                    """)
                .ToListAsync())
            .ToDictionary(s => s[..s.IndexOf(' ')], s => s[(s.IndexOf(' ') + 1)..]);

        Assert.Contains("IX_comments_post_id", indexes.Keys);
        Assert.DoesNotContain("IX_comments_parent_id", indexes.Keys);
        Assert.Contains("(parent_id, created_at, comment_id)", indexes["idx_comments_parent"]);
        Assert.Contains("(post_id, created_at, comment_id)", indexes["idx_comments_post_roots"]);
        Assert.EndsWith("WHERE (parent_id IS NULL)", indexes["idx_comments_post_roots"]);
    }

    /// <summary>
    /// A3 (GĐ3) — <c>ck_comments_root_depth</c> chặn hai lỗi rẻ nhất (gốc mang depth 2, phản hồi mang depth 1);
    /// <c>ck_comments_status</c> nhận <c>hidden</c> (Đ-6.14); bộ đếm mới của bình luận mặc định <c>0</c> / <c>{}</c>.
    /// </summary>
    [Fact]
    public async Task Check_comments_GD3_chan_goc_sai_cap_va_nhan_hidden()
    {
        var (_, connectionString) = await MigratedAsync(postgres);
        await using var conn = await OpenAsync(connectionString);

        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand(
            """
            insert into content.posts (post_id, author_id, body, media_count, created_at, updated_at)
            values (@p, gen_random_uuid(), 'bài', 0, now(), now());
            insert into content.comments (comment_id, post_id, parent_id, author_id, depth, body, status)
            values (@r, @p, null, gen_random_uuid(), 1, 'gốc', 'hidden');
            """, conn))
        {
            seed.Parameters.AddWithValue("p", postId);
            seed.Parameters.AddWithValue("r", rootId);
            await seed.ExecuteNonQueryAsync();
        }

        async Task<PostgresException> InsertBadAsync(Guid? parentId, short depth)
        {
            await using var cmd = new NpgsqlCommand(
                """
                insert into content.comments (comment_id, post_id, parent_id, author_id, depth, body)
                values (gen_random_uuid(), @p, @parent, gen_random_uuid(), @d, 'sai cấp')
                """, conn);
            cmd.Parameters.AddWithValue("p", postId);
            cmd.Parameters.AddWithValue("parent", (object?)parentId ?? DBNull.Value).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            cmd.Parameters.AddWithValue("d", depth);
            return await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        }

        Assert.Equal("ck_comments_root_depth", (await InsertBadAsync(null, 2)).ConstraintName);
        Assert.Equal("ck_comments_root_depth", (await InsertBadAsync(rootId, 1)).ConstraintName);

        await using var read = new NpgsqlCommand(
            "select reply_count, reaction_counts::text from content.comments where comment_id = @r", conn);
        read.Parameters.AddWithValue("r", rootId);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal("{}", reader.GetString(1));
    }

    /// <summary>A3 — <c>--migrate</c> chạy lần hai trên DB đã đủ migration thì không làm gì và không lỗi.</summary>
    [Fact]
    public async Task Migrate_lan_hai_khong_doi_gi()
    {
        var (services, _) = await MigratedAsync(postgres);

        await services.MigrateContentModuleAsync();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains("Gd3Interactions", string.Join(",", await db.Database.GetAppliedMigrationsAsync()));
    }
}
