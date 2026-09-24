using Microsoft.Extensions.Logging;
using SocialApp.Modules.Content.Application.Feed;
using SocialApp.Modules.Content.Application.Reactions;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Observability;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Nghiệp vụ ghi của bài đăng. Nhận <see cref="IPostStore"/> chứ không nhận <c>ContentDbContext</c>, và bốn bề mặt trừu
/// tượng của SharedKernel/BCL — nhờ vậy lớp này test được mà không cần Postgres lẫn R2.
///
/// C4 (GĐ4, Đ-4.8): cả ba thao tác ghi xóa khóa <c>feed:p1:</c> của TÁC GIẢ sau khi lưu thành công — với tác giả, trang đầu
/// cache 30s trông như "đăng bài không lên". Truyền <see cref="PostCommit"/> chứ không truyền token của request: bài đã
/// lưu thì client ngắt không được bỏ dở việc xóa (cùng bài học của <c>RelationshipService</c>).
/// </summary>
public sealed class PostService(
    IPostStore posts,
    IUserDirectory directory,
    IObjectStorage storage,
    IReactionReader reactions,
    PostResponseMapper mapper,
    IFeedPageCache feedPageCache,
    TimeProvider clock,
    ILogger<PostService> logger)
{
    /// <summary>Token cho việc chạy SAU khi đã lưu — xem phần đầu lớp.</summary>
    private static readonly CancellationToken PostCommit = CancellationToken.None;

    /// <summary>
    /// <c>POST /posts</c> — đường dài nhất của giai đoạn (SEQ-01 bước 6–7).
    ///
    /// <b>THỨ TỰ SÁU BƯỚC DƯỚI ĐÂY LÀ MỘT PHẦN CỦA HỢP ĐỒNG</b>, không phải sở thích sắp xếp: nó quyết định mã lỗi mà
    /// người dùng nhận, và <c>content-v1.yaml</c> đã ghi đúng thứ tự này trong <c>description</c> của operation. Đảo hai
    /// bước bất kỳ là đổi hành vi công khai của API mà không đổi một dòng hợp đồng nào.
    ///
    /// <list type="number">
    /// <item><b>Tầng 2 — quyền <c>post.create</c></b>: đã xong ở controller (<c>[RequirePermission]</c>).</item>
    /// <item><b>Tầng 3 — có hồ sơ chưa</b> (Đ-2.4) → <b>403</b>.</item>
    /// <item><b>Tầng 3 — mọi key thuộc <c>posts/{actorId}/</c></b> (Đ-2.7) → <b>403</b>. <c>TC-A03-media</c> canh.</item>
    /// <item><b>BR-01</b> → <b>400</b> theo <c>body</c> hoặc <c>mediaKeys</c>.</item>
    /// <item><b>Đ-2.8 lớp 2 — HEAD từng object</b> → <b>400</b> <c>mediaKeys</c>, KHÔNG tạo bài.</item>
    /// <item><b>Một transaction</b> INSERT post + media; đụng UNIQUE <c>storage_key</c> → <b>409</b>.</item>
    /// </list>
    ///
    /// Hai chỗ quan trọng nhất và lý do:
    /// <list type="bullet">
    /// <item><b>Bước 3 đứng TRƯỚC bước 5.</b> Kẻ dò key của người khác không được tiêu một lời gọi R2 nào — R2 tính tiền
    /// theo lời gọi, và đảo thứ tự biến endpoint này thành máy bơm hóa đơn. Cùng lập luận với <c>D3</c>.</item>
    /// <item><b>Bước 5 đứng TRƯỚC bước 6 và nằm ở lớp khác.</b> HEAD ở service, transaction ở store, hai hàm khác nhau —
    /// gộp lại là "có bài rồi mới phát hiện ảnh sai" rồi phải rollback thủ công. Đây là điều 2 trong năm thứ B.9 nói
    /// KHÔNG test tự động nào bắt được; ranh giới hai hàm là lưới duy nhất.</item>
    /// </list>
    ///
    /// Hai đường 403 (chưa có hồ sơ, key của người khác) dùng CHUNG <see cref="Result{T}.Forbidden"/>: status code không
    /// lộ gì thì thông điệp cũng không được lộ — hợp đồng ghi rõ "cùng một phản hồi, không nêu lý do nào".
    /// </summary>
    public async Task<Result<PostResponse>> CreateAsync(Guid actorId, CreatePostRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (2) Đ-2.4. MỘT lời gọi batch (contract không có bản đơn — Đ-2.3 luật 3), và UserCard lấy được ở đây dùng lại
        // cho `author` của response: không phải hỏi lần thứ hai.
        var cards = await directory.GetManyAsync([actorId], ct);
        if (!cards.TryGetValue(actorId, out var author))
            return Result<PostResponse>.Forbidden();

        var media = request.MediaKeys ?? [];

        // (3) Đ-2.7 — TC-A03-media. `Any` dừng ở key sai đầu tiên; không nêu key nào sai trong phản hồi (nó là dữ liệu
        // của người khác).
        if (media.Exists(m => !StorageKeys.BelongsTo(m.MediaKey, StorageKeys.PostsPrefix, actorId)))
            return Result<PostResponse>.Forbidden();

        // (4) BR-01 lần nữa bằng hàm thuần — rẻ, và service không giả định mình LUÔN được gọi qua MVC (GĐ sau có thể
        // gọi từ worker hay từ một endpoint khác, lúc đó auto-validation không chạy).
        var br01 = PostContentPolicy.Validate(request.Body, media.Count);
        if (!br01.IsValid)
            return ContentErrors.FromValidation(br01);

        // Cùng lý do: validator đã chặn key trùng, nhưng UNIQUE `storage_key` sẽ nổ thành 409 "ảnh đã dùng ở bài khác"
        // nếu lọt xuống DB — sai hẳn nguyên nhân, vì "bài khác" đó chính là bài đang gửi.
        if (media.Select(m => m.MediaKey).Distinct(StringComparer.Ordinal).Count() != media.Count)
            return ContentErrors.DuplicateMediaKeys;

        // (5) Đ-2.8 lớp 2 — HEAD TỪNG key, dừng ở lỗi đầu tiên, TRƯỚC khi chạm store.
        foreach (var declaration in media)
        {
            var head = await storage.HeadAsync(declaration.MediaKey, ct);
            var check = MediaHeadPolicy.Check(
                new MediaDeclaration(declaration.ContentType, declaration.SizeBytes), head);
            if (!check.IsValid)
                return ContentErrors.FromValidation(check);
        }

        // (6) media_count ĐÚNG ngay từ INSERT: INSERT 0 rồi UPDATE sau thì `ck_posts_not_empty` nổ ngay ở câu INSERT
        // với bài chỉ có ảnh (BR01-06).
        var now = clock.GetUtcNow();
        var post = new Post
        {
            AuthorId = actorId,
            Body = NormalizeBody(request.Body),
            Privacy = request.Privacy!.Value,   // validator đã NotNull (Q-D2); action không bao giờ thấy request chưa hợp lệ
            MediaCount = (short)media.Count,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var attachments = media.Select((m, index) => new MediaAttachment
        {
            OwnerType = MediaOwnerType.Post,
            OwnerId = post.PostId,
            StorageKey = m.MediaKey,
            ContentType = m.ContentType,
            SizeBytes = (int)m.SizeBytes,   // an toàn CHỈ nhờ validator đã chặn ≤ 10 MB — xem MediaKeyDeclaration.SizeBytes
            Position = (short)index,        // thứ tự trong mảng = position hiển thị, đúng hợp đồng
            CreatedAt = now,
        }).ToList();

        if (!await posts.AddWithMediaAsync(post, attachments, ct))
            return ContentErrors.MediaAlreadyUsed;   // 409, POST-08

        BusinessMetrics.PostCreated();   // SAU khi lưu thành công — không đếm nhánh bị từ chối ở trên (GĐ7 C2)
        await feedPageCache.InvalidateAsync(actorId, PostCommit);

        // Không key, không URL (Mục 1.3 luật 9).
        logger.LogInformation("Đã tạo bài {PostId} với {MediaCount} ảnh", post.PostId, post.MediaCount);

        // Bài vừa tạo: chưa ai thả cảm xúc, kể cả tác giả — không cần hỏi DB (D7 GĐ3).
        return mapper.ToResponse(post, attachments, author, myReaction: null, actorId);
    }

    /// <summary>
    /// <c>PATCH /posts/{postId}</c> — sửa <c>body</c> và/hoặc <c>privacy</c> của bài MÌNH, theo đúng khuôn tầng 3 của
    /// Mục 6.2.
    ///
    /// <b>Ba lý do trượt, MỘT phản hồi 403</b> (quy ước 3b, <c>TC-A03</c>): bài không tồn tại, bài của người khác, bài
    /// đã xóa mềm (query filter loại sẵn nên nó đến đây dưới dạng <c>null</c>). Khác đường ĐỌC của D6 — ở đó ba lý do
    /// trượt cho 404 — và sự khác nhau đó là cố ý: thao tác GHI cần ownership trả 403, thao tác ĐỌC nội dung có mức
    /// hiển thị trả 404 (Mục 6.1).
    ///
    /// <b>KHÔNG có nhánh <c>role == ADMIN</c> ở đây</b> (Mục 3.2 của GĐ1). Lối tắt Admin chỉ tồn tại ở tầng 2, trong
    /// <c>PermissionHandler</c>; lặp lại nó ở tầng 3 nghĩa là "Admin sửa được bài của bất kỳ ai" — đúng lỗ IDOR mà
    /// GOAL-03 muốn đóng.
    ///
    /// <c>editedAt</c> đóng dấu ở đây; <c>updatedAt</c> do <c>ContentDbContext.SaveChangesAsync</c> đóng (A5) — không
    /// gán tay, hai nguồn thời gian cho một cột là hai nguồn lệch được.
    ///
    /// GĐ2 KHÔNG sửa ảnh (Mục 7.3): <see cref="UpdatePostRequest"/> không có trường nào cho ảnh, nên
    /// <c>media_count</c> giữ nguyên và BR-01 dưới đây dùng chính con số THẬT của bài.
    /// </summary>
    public async Task<Result<PostResponse>> UpdateAsync(
        Guid postId, Guid actorId, UpdatePostRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var post = await posts.FindForUpdateAsync(postId, ct);

        // Tầng 3. Ba lý do, một phản hồi — xem phần đầu.
        if (post is null || post.AuthorId != actorId)
            return Result<PostResponse>.Forbidden();

        // BR-07 (Đ-6.14): SAU tầng 3 — người khác vẫn nhận 403, không biết bài bị ẩn. TRƯỚC BR-01: sửa nội dung rồi "tự gỡ
        // ẩn" là lách kiểm duyệt, nên không bước nào dưới đây được chạy với bài bị ẩn. DeleteAsync KHÔNG có nhánh này.
        if (post.Status == PostStatus.Hidden)
            return ContentErrors.PostHidden;

        // `null` = KHÔNG GỬI (System.Text.Json không phân biệt với vắng mặt). Muốn xóa chữ thì gửi "" — và lúc đó
        // BR-01 quyết định: bài có ảnh thì được, bài chỉ chữ thì 400.
        if (request.Body is not null)
        {
            var body = NormalizeBody(request.Body);
            var br01 = PostContentPolicy.Validate(body, post.MediaCount);
            if (!br01.IsValid)
                return ContentErrors.FromValidation(br01);

            post.Body = body;
        }

        if (request.Privacy is { } privacy)
            post.Privacy = privacy;

        post.EditedAt = clock.GetUtcNow();
        await posts.SaveAsync(ct);
        await feedPageCache.InvalidateAsync(actorId, PostCommit);

        logger.LogInformation("Đã sửa bài {PostId}", post.PostId);

        // Đọc lại ảnh và tác giả để trả nguyên PostResponse — FE không phải gọi thêm GET sau khi sửa.
        var attachments = await posts.MediaOfAsync([post.PostId], ct);
        var cards = await directory.GetManyAsync([post.AuthorId], ct);
        var mine = await reactions.GetMineAsync(actorId, ReactionTargetType.Post, [post.PostId], ct);

        return mapper.ToResponse(
            post,
            attachments.TryGetValue(post.PostId, out var media) ? media : [],
            cards.GetValueOrDefault(post.AuthorId),
            mine.TryGetValue(post.PostId, out var reaction) ? reaction : null,
            actorId);
    }

    /// <summary>
    /// <c>DELETE /posts/{postId}</c> — <b>xóa MỀM</b> (Đ-2.10): đặt <c>status = 'deleted'</c> + <c>deleted_at</c>, không
    /// <c>DELETE</c> dòng nào.
    ///
    /// Bài bị ẩn (BR-07) xóa ĐƯỢC — khác <see cref="UpdateAsync"/>: người dùng luôn xóa được nội dung của mình (Đ-6.14).
    ///
    /// Tầng 3 y hệt <see cref="UpdateAsync"/>, và <b>gọi lần hai trả 403 một cách TỰ NHIÊN</b>: global query filter đã
    /// loại bài <c>deleted</c> nên <see cref="IPostStore.FindForUpdateAsync"/> trả <c>null</c> ở lượt thứ hai, rơi đúng
    /// vào nhánh <see cref="Result.Forbidden"/> sẵn có. Viết <c>if (post.Status == Deleted) return NotFound</c> là đi
    /// vòng — và ra sai mã, vì bảng Mục 6.1 chốt thao tác GHI cần ownership dùng 403.
    ///
    /// <b>KHÔNG xóa gì trên R2, KHÔNG xóa dòng <c>media_attachments</c></b> (Đ-2.10, Đ-2.13). Hai lý do, cả hai đều
    /// nặng: bấm nhầm là mất ảnh vĩnh viễn, và <c>MediaCleanupWorker</c> (C4) tìm object mồ côi CHÍNH BẰNG những dòng
    /// <c>media_attachments</c> còn lại. Dọn là việc của worker sau 7 ngày.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid postId, Guid actorId, CancellationToken ct)
    {
        var post = await posts.FindForUpdateAsync(postId, ct);

        // Ba lý do trượt, một phản hồi — trong đó "đã xóa rồi" đến đây dưới dạng null nhờ query filter.
        if (post is null || post.AuthorId != actorId)
            return Result.Forbidden();

        post.Status = PostStatus.Deleted;
        post.DeletedAt = clock.GetUtcNow();
        await posts.SaveAsync(ct);
        await feedPageCache.InvalidateAsync(actorId, PostCommit);

        logger.LogInformation("Đã xóa mềm bài {PostId}", post.PostId);

        return Result.Success();
    }

    /// <summary>
    /// Rỗng hoặc toàn khoảng trắng → <c>null</c>, để <c>body</c> của bài chỉ có ảnh là <c>null</c> đúng như ví dụ hợp
    /// đồng, và để nhất quán với <c>ck_posts_not_empty</c> (DB so <c>btrim(coalesce(body,''))</c>) lẫn với
    /// <see cref="PostContentPolicy.Validate"/> (dùng <c>IsNullOrWhiteSpace</c>). Ba nơi cùng coi bốn giá trị
    /// — vắng mặt, <c>null</c>, <c>""</c>, <c>"   "</c> — là một.
    ///
    /// KHÔNG <c>Trim()</c> nội dung có chữ: người dùng xuống dòng đầu bài là cố ý, và đây là văn bản tự do chứ không
    /// phải một cái tên (khác <c>displayName</c> của D2, thứ hợp đồng đo "sau khi trim").
    /// </summary>
    private static string? NormalizeBody(string? body) => string.IsNullOrWhiteSpace(body) ? null : body;
}
