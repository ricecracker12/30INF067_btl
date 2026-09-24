using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Body 200/201 của MỌI endpoint trả một bài (<c>POST /posts</c> ở D5, <c>GET /posts/{postId}</c> ở D6,
/// <c>PATCH /posts/{postId}</c> ở D7) — khớp schema <c>PostResponse</c> của <c>content-v1.yaml</c>. Một hình dạng, không
/// có bản rút gọn: FE chỉ sinh một kiểu và <c>PostPage.items</c> dùng lại chính nó.
///
/// Là <c>record</c> riêng chứ KHÔNG trả entity <see cref="Post"/>: entity mang <c>Status</c>, <c>HiddenReason</c>,
/// <c>DeletedAt</c> — trạng thái kiểm duyệt nội bộ — và Swagger sẽ sinh schema có những trường đó, làm cổng hợp đồng (B4)
/// đỏ. <c>media_attachments</c> cũng không lộ <c>storage_key</c> ra ngoài: xem <see cref="PostMedia"/>.
/// </summary>
/// <param name="Body"><c>null</c> khi bài chỉ có ảnh — chuẩn hóa ở service, xem <c>PostService.NormalizeBody</c>.</param>
/// <param name="CommentCount">GĐ2 luôn 0; có mặt từ bây giờ để hình dạng DTO không đổi lần hai ở GĐ3 (Đ-2.12).</param>
/// <param name="ReactionCounts">
/// GĐ2 luôn <c>{}</c> rỗng, <b>không bao giờ</b> <c>null</c> (Đ-2.12, Mục 8.2). Đổi sang <c>null</c> "cho gọn" là làm vỡ
/// màn E5 ở GĐ3 — FE viết <c>Object.entries(reactionCounts)</c> một lần và không phân nhánh.
/// </param>
/// <param name="EditedAt"><c>null</c> = chưa sửa lần nào. D7 đóng dấu.</param>
/// <param name="CanEdit">
/// Do SERVER tính (<c>author_id == actorId</c>), không phải FE tự so id (Mục 8.2). Ở GĐ2 nó trùng với "được sửa/xóa";
/// GĐ6 thêm vai trò kiểm duyệt thì chính chỗ này đổi, và FE không phải biết.
/// </param>
/// <param name="Moderation">
/// GĐ6 D7a (Đ-6.14, BR-07): khác <c>null</c> CHỈ khi bài bị ẩn và người gọi là tác giả — người khác không bao giờ tới được đây
/// với bài <c>hidden</c> (404 ở <c>PostReadService</c>, feed/trang cá nhân lọc từ GĐ4). Đặt CUỐI record: A (GĐ3) cũng thêm trường
/// vào đây, ai thêm cũng đặt cuối để rebase không chạm dòng của nhau (Mục 9.4).
/// </param>
public sealed record PostResponse(
    Guid PostId,
    PostAuthor Author,
    string? Body,
    PostPrivacy Privacy,
    IReadOnlyList<PostMedia> Media,
    int CommentCount,
    IReadOnlyDictionary<string, int> ReactionCounts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool CanEdit,
    PostModeration? Moderation);

/// <summary>
/// Trạng thái kiểm duyệt của bài, cho biểu ngữ "bài bị ẩn" của tác giả (E9). Không có id Moderator, không có ghi chú quyết định:
/// tác giả biết bài bị ẩn và vì sao, không biết ai ẩn.
/// </summary>
/// <param name="Status">Luôn <c>"hidden"</c> ở GĐ6 — chuỗi chứ không bool để thêm trạng thái sau không đổi hình dạng.</param>
/// <param name="ReasonCode">Mã lý do của báo cáo được xử lý (<c>ReasonCode</c> của <c>moderation-v1</c>), không phải câu chữ.</param>
/// <param name="HiddenAt">
/// <c>updated_at</c> của lần ẩn (Mục 4: không có cột <c>hidden_at</c>) — <c>HideAsync</c> (C2) đóng dấu nó, và tác giả không sửa
/// được bài bị ẩn nên không gì đóng dấu lại. KHÔNG phải <c>EditedAt</c>: đó là lần sửa cuối của tác giả.
/// </param>
public sealed record PostModeration(string Status, string ReasonCode, DateTimeOffset HiddenAt);

/// <summary>
/// Tác giả, dựng từ <c>IUserDirectory</c> (SharedKernel, A6) — MỘT lời gọi batch cho cả trang (Đ-2.3).
/// </summary>
/// <param name="AvatarUrl">Presigned GET 15 phút (Đ-2.9), <c>null</c> khi chưa có avatar. Ký ở mapper, không ở store.</param>
public sealed record PostAuthor(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>
/// Một ảnh của bài. <b>Không có <c>mediaKey</c></b> và đó là cố ý (hợp đồng ghi rõ): key là chi tiết nội bộ, và lộ nó ra
/// là đưa cho client một chuỗi đoán được tiền tố người dùng khác.
/// </summary>
/// <param name="Url">Presigned GET 15 phút, cấp SAU khi đã qua BR-02 (Đ-2.9).</param>
/// <param name="Width">Kích thước ảnh, <c>null</c> khi chưa đọc được — GĐ2 không đọc, luôn <c>null</c>.</param>
public sealed record PostMedia(string Url, string ContentType, int? Width, int? Height, int Position);
