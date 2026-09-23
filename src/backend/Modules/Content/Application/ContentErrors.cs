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
/// Không có lỗi 403 nào trong danh sách này: mọi 403 của Content (thiếu quyền, chưa có hồ sơ, key sai tiền tố, không phải bài
/// của mình, bài đã xóa mềm) dùng <see cref="Error.Forbidden"/> của SharedKernel — hợp đồng đã chốt "cùng một phản hồi, không
/// nêu lý do nào" (content-v1.yaml, 403 của POST /posts).
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
}
