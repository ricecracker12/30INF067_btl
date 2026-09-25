using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Application;

/// <summary>
/// Mọi <see cref="Error"/> của module Content ở MỘT chỗ — cùng lý do với <c>IdentityErrors</c> của GĐ1: rà PII bằng một lần
/// đọc ở D9 (không thông điệp nào chứa postId, storage key hay tên kiểu .NET), và một loại lỗi chỉ có đúng một cách nói.
///
/// Thông điệp chép ĐÚNG câu trong <c>example</c> của <c>content-v1.yaml</c>, hoặc lấy thẳng từ hằng số của Domain
/// (<see cref="PostContentPolicy"/>, <see cref="MediaHeadPolicy"/>) — không gõ lại câu ở tầng này.
///
/// 403 của Content (thiếu quyền, key sai tiền tố, không phải bài của mình, bài đã xóa mềm) dùng <see cref="Error.Forbidden"/> của
/// SharedKernel — "cùng một phản hồi, không nêu lý do nào". Ngoại lệ DUY NHẤT: <see cref="ProfileRequired"/> của <c>POST /posts</c>
/// (GĐ6, sửa 2026-09-25) — xem lý do ở chính nó.
/// </summary>
public static class ContentErrors
{
    /// <summary>
    /// 404 của <c>GET /posts/{postId}</c>. Chỉ cho THAO TÁC ĐỌC: bài không tồn tại, đã xóa mềm, hay BR-02 không cho xem đều
    /// cùng mã này (Mục 7.4). Thao tác GHI cần ownership thì trả <see cref="Error.Forbidden"/>, không trả 404 — 404 cho một
    /// bên và 403 cho bên kia là status code tự khai tài nguyên có tồn tại.
    /// </summary>
    public static readonly Error PostNotFound = new("post.not_found", "Không tìm thấy bài viết.", 404);

    /// <summary>
    /// 409 của <c>POST /posts</c> khi UNIQUE <c>storage_key</c> nổ (POST-08): một object R2 chỉ được đính vào một bài.
    /// Store bắt <c>PostgresException 23505</c> rồi trả lỗi này — không để exception chảy lên thành 500.
    /// </summary>
    public static readonly Error MediaAlreadyUsed = new("post.media_conflict", "Ảnh này đã được dùng trong một bài khác.", 409);

    /// <summary>
    /// Cầu nối Domain → HTTP cho hai hàm thuần <see cref="PostContentPolicy.Validate"/> và <see cref="MediaHeadPolicy.Check"/>:
    /// chúng cố ý KHÔNG biết mã HTTP, còn tầng này quyết định "BR-01 hỏng và HEAD lệch đều là 400 theo trường".
    /// Gọi khi <c>IsValid</c> là false — key và thông điệp lúc đó chắc chắn khác null.
    /// </summary>
    public static Error FromValidation(PostContentValidation validation) =>
        Error.Validation(validation.ErrorKey!, validation.Message!);

    /// <summary>
    /// 400 của <c>PATCH /posts/{postId}</c> với body <c>{}</c> — không trường nào để sửa. Key là <c>body</c> vì đó là ô người
    /// dùng đang đứng (cùng lập luận với <see cref="PostContentPolicy.Empty"/>).
    /// </summary>
    public static Error NothingToUpdate => Error.Validation(PostContentPolicy.BodyKey, NothingToUpdateMessage);

    /// <summary>
    /// Câu của <see cref="NothingToUpdate"/>, tách thành hằng ở D7 để <c>UpdatePostRequestValidator</c> dùng CHUNG —
    /// cùng lý do với <see cref="DuplicateMediaKeysMessage"/>.
    /// </summary>
    public const string NothingToUpdateMessage = "Không có gì để sửa.";

    /// <summary>
    /// 400 của <c>POST /posts</c> khi <c>mediaKeys</c> có hai phần tử trùng key. Bắt TRƯỚC transaction: để nó chạy tới DB thì
    /// UNIQUE <c>storage_key</c> nổ và người dùng nhận 409 "ảnh đã dùng ở bài khác" — sai hẳn nguyên nhân.
    /// </summary>
    public static Error DuplicateMediaKeys =>
        Error.Validation(PostContentPolicy.MediaKeysKey, DuplicateMediaKeysMessage);

    /// <summary>
    /// Câu của <see cref="DuplicateMediaKeys"/>, tách ra thành hằng ở D5 để <c>CreatePostRequestValidator</c> dùng CHUNG.
    /// Hai chỗ cùng bắt một loại lỗi (validator trước action, service phòng khi không được gọi qua MVC) mà gõ hai câu là
    /// một loại lỗi có hai cách nói.
    /// </summary>
    public const string DuplicateMediaKeysMessage = "Một ảnh không được đính kèm hai lần.";

    /// <summary>
    /// 503 của <c>GET /feed</c> khi truy vấn feed vượt 5s (Đ-4.10, UC-08 luồng E3): "đông quá, thử lại", không phải 500 "hệ
    /// thống hỏng". <c>feed.unavailable</c> là mã NỘI BỘ — Problem Details không mang <c>Error.Code</c>. Title đặt riêng vì
    /// bảng mặc định gộp mọi <c>&gt;= 500</c> vào "Đã xảy ra lỗi không mong muốn" — sai nghĩa với 503. Header
    /// <c>Retry-After: 5</c> gắn ở controller: <c>Result → Problem</c> không gắn header nào. Không hứa thời điểm trong câu chữ.
    ///
    /// <c>type</c> riêng (<see cref="FeedOverloadedType"/>, GĐ4 Q-E4): 503 tới trình duyệt còn có thể là BFF mất kho phiên —
    /// FE phân nhánh theo <c>type</c>, không theo <c>title</c> (nhãn hiển thị, không phải định danh).
    /// </summary>
    public static readonly Error FeedUnavailable = new(
        "feed.unavailable",
        "Bảng tin đang có quá nhiều người truy cập. Vui lòng thử lại.",
        503,
        Title: "Bảng tin đang quá tải",
        Type: FeedOverloadedType);

    /// <summary><c>type</c> của 503 feed quá tải — khai ở <c>FeedOverloadedProblem</c> trong <c>content-v1.yaml</c>.</summary>
    public const string FeedOverloadedType = "urn:socialapp:problem:feed-overloaded";

    /// <summary>
    /// 409 của <c>PATCH /posts/{postId}</c> khi TÁC GIẢ sửa bài bị ẩn (GĐ6 D7a, Đ-6.14): sửa nội dung rồi "tự gỡ ẩn" là lách
    /// kiểm duyệt. Chỉ tác giả thấy được lỗi này — người khác dừng ở 403 của tầng 3, không biết bài bị ẩn. <c>type</c> riêng
    /// (<see cref="PostHiddenType"/>): 409 của Content còn một nghĩa khác (<see cref="MediaAlreadyUsed"/> ở <c>POST /posts</c>).
    /// </summary>
    public static readonly Error PostHidden = new(
        "post.hidden",
        "Bài viết đã bị ẩn do vi phạm tiêu chuẩn cộng đồng nên không sửa được.",
        409,
        Type: PostHiddenType);

    /// <summary><c>type</c> của 409 bài bị ẩn — khai ở <c>PostHiddenProblem</c> trong <c>content-v1.yaml</c> (Mục 17.1).</summary>
    public const string PostHiddenType = "urn:socialapp:problem:post-hidden";

    /// <summary>
    /// 403 của <c>POST /posts</c> khi người gọi CHƯA CÓ HỒ SƠ (Đ-2.4) — <c>type</c> riêng (<see cref="ProfileRequiredType"/>), sửa
    /// 2026-09-25 (nợ phát hiện ở GĐ6 E2E-05). Trước đó mọi 403 của endpoint này trùng một phản hồi, và FE nói "hãy hoàn tất hồ sơ"
    /// cả khi vai trò vừa bị Admin gỡ <c>post.create</c> — người dùng không có cách nào hiểu đúng.
    ///
    /// Không lộ gì: người gọi luôn biết mình có hồ sơ hay chưa (<c>GET /users/{mình}/profile</c> trả 404), và nhánh này chạy TRƯỚC
    /// kiểm tiền tố <c>mediaKey</c> — người chưa có hồ sơ nhận nó bất kể key của ai, nên nó không nói gì về dữ liệu người khác. 403
    /// thiếu quyền (tầng 2) và 403 key sai tiền tố (Đ-2.7) vẫn trùng một phản hồi như cũ.
    /// </summary>
    public static readonly Error ProfileRequired = new(
        "post.profile_required",
        "Bạn cần tạo hồ sơ trước khi đăng bài.",
        403,
        Type: ProfileRequiredType);

    /// <summary><c>type</c> của 403 chưa có hồ sơ — khai ở <c>ProfileRequiredProblem</c> trong <c>content-v1.yaml</c>.</summary>
    public const string ProfileRequiredType = "urn:socialapp:problem:profile-required";

    // ---- GĐ3: bình luận + cảm xúc ----

    /// <summary>
    /// 404 của mọi endpoint nhận <c>commentId</c> (Đ-3.3, Mục 6.2): bình luận không tồn tại, không còn hiển thị (với cảm xúc),
    /// hay nằm trong bài người gọi không được xem — MỘT <see cref="Error"/>, một câu. Endpoint nhận <c>postId</c> dùng
    /// <see cref="PostNotFound"/>: cùng một đường dẫn luôn nói cùng một câu, dù lý do trượt là gì.
    /// </summary>
    public static readonly Error CommentNotFound = new("comment.not_found", "Không tìm thấy bình luận.", 404);

    /// <summary>400 <c>errors.parentId</c>: cha không tồn tại, thuộc bài khác, hay không còn hiển thị (Đ-3.4).</summary>
    public static Error ParentGone => Error.Validation(CommentDepthPolicy.ParentIdKey, CommentDepthPolicy.ParentGone);

    /// <summary>Cầu nối Domain → HTTP cho <see cref="CommentPolicy.Validate"/> và <see cref="CommentDepthPolicy.ValidateReply"/>.</summary>
    public static Error FromValidation(CommentValidation validation) =>
        Error.Validation(validation.ErrorKey!, validation.Message!);

    /// <inheritdoc cref="FromValidation(CommentValidation)"/>
    public static Error FromValidation(CommentDepthValidation validation) =>
        Error.Validation(validation.ErrorKey!, validation.Message!);
}
