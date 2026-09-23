using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Đ-2.8 lớp 2 dưới dạng HÀM THUẦN — cùng nếp <see cref="PostContentPolicy"/>: đầu vào là giá trị, đầu ra là kết quả,
/// không I/O. Lệnh <c>HEAD</c> lên R2 là việc của người gọi (D5 qua <c>IObjectStorage.HeadAsync</c>); hàm này chỉ đối
/// chiếu thứ HEAD trả về với thứ client đã khai lúc presign. Nhờ vậy ba nhánh hỏng kiểm được bằng unit test với hai
/// record, không cần mạng, không cần cả FakeObjectStorage.
///
/// Vì sao lớp này tồn tại dù đã ký <c>Content-Length</c>/<c>Content-Type</c> vào URL (lớp 1): chữ ký nói "client ĐỊNH
/// PUT cái gì", còn HEAD nói "trong bucket ĐANG CÓ cái gì" — hai thứ khác nhau khi client chưa PUT xong, PUT hỏng giữa
/// chừng, hay dùng lại một key cũ. Lớp 2 là lớp duy nhất đọc đúng thứ nằm trong bucket. Lưới cuối là CHECK ở DB (lớp 3).
///
/// Ba nhánh hỏng, ba thông điệp, CÙNG một mã 400 với key <see cref="PostContentPolicy.MediaKeysKey"/> (Mục 6.1: "400 HEAD
/// lệch"). Mã HTTP là quyết định của tầng D, không của Domain — nên trả <see cref="PostContentValidation"/>, không trả
/// <c>Result</c>/<c>Error</c>.
///
/// Hai thứ KHÔNG thuộc về đây, đừng thêm vào:
/// <list type="bullet">
/// <item>Kiểm tiền tố <c>posts/{actorId}/</c> của key — đó là Đ-2.7, trả <b>403</b>, và <c>TC-A03-media</c> canh nó (D5).</item>
/// <item>Sniff nội dung file để biết "có thật là JPEG không". <c>ContentType</c> từ HEAD là thứ client đặt lúc PUT; hàm này
/// chỉ khẳng định "khai lúc presign" khớp "khai lúc PUT". Lớp chặn loại file là allowlist ở C2 cộng CHECK ở DB.</item>
/// </list>
///
/// Người gọi (D5) phải HEAD từng key và gọi hàm này <b>TRƯỚC</b> khi mở transaction INSERT post + media. Đảo lại thì
/// có bài rồi mới phát hiện ảnh sai, và phải rollback thủ công — đây là một trong năm thứ B.9 nói không test tự động
/// nào bắt được, comment này là lưới duy nhất.
/// </summary>
public static class MediaHeadPolicy
{
    public const string NotUploaded = "Ảnh chưa được tải lên xong. Hãy chờ tải lên hoàn tất rồi đăng lại.";
    public const string SizeMismatch = "Dung lượng ảnh không khớp với khai báo lúc xin tải lên.";
    public const string TypeMismatch = "Loại ảnh không khớp với khai báo lúc xin tải lên.";

    /// <summary>
    /// Đối chiếu khai báo lúc presign với kết quả HEAD của chính key đó.
    /// </summary>
    /// <param name="declared">Thứ client khai ở <c>POST /media/uploads</c> và server đã ký vào URL.</param>
    /// <param name="actual">Kết quả <c>HeadAsync</c>; <c>null</c> nghĩa là object không có trong bucket.</param>
    /// <returns>
    /// <see cref="PostContentValidation.Valid"/>, hoặc kết quả mang key <see cref="PostContentPolicy.MediaKeysKey"/> và một trong
    /// ba thông điệp. Kiểm theo thứ tự tồn tại → dung lượng → loại: object chưa có thì hai câu sau vô nghĩa.
    /// </returns>
    public static PostContentValidation Check(MediaDeclaration declared, ObjectHead? actual)
    {
        if (actual is null)
            return new PostContentValidation(PostContentPolicy.MediaKeysKey, NotUploaded);

        if (actual.ContentLength != declared.SizeBytes)
            return new PostContentValidation(PostContentPolicy.MediaKeysKey, SizeMismatch);

        // R2 trả lại đúng chuỗi client đặt lúc PUT; "Image/JPEG" và "image/jpeg" là cùng một loại theo RFC 2045.
        if (!string.Equals(actual.ContentType, declared.ContentType, StringComparison.OrdinalIgnoreCase))
            return new PostContentValidation(PostContentPolicy.MediaKeysKey, TypeMismatch);

        return PostContentValidation.Valid;
    }
}

/// <summary>
/// Thứ client khai cho MỘT file lúc xin presign (SEQ-01 bước 2) — và là thứ server đã ký vào URL. D5 dựng lại từ
/// <c>mediaKeys</c> của <c>POST /posts</c> (hình dạng do hợp đồng <c>content-v1.yaml</c> chốt ở cổng mở).
/// </summary>
public readonly record struct MediaDeclaration(string ContentType, long SizeBytes);
