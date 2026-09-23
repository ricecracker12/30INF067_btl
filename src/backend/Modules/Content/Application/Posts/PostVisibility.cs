using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// BR-02 dưới dạng HÀM THUẦN — cùng nếp <see cref="PostContentPolicy"/> và <see cref="MediaHeadPolicy"/>: đầu vào là giá
/// trị, đầu ra là kết quả, không I/O. Nhờ vậy ma trận <c>READ-02..05</c> kiểm được bằng unit test.
///
/// <b>"Tại thời điểm đọc"</b> (Mục 7.4) nghĩa là không có bản sao quyền xem nào bị đóng băng lúc ghi: đổi <c>privacy</c>
/// của bài cũ có hiệu lực ngay ở request kế tiếp, và sang GĐ4 khi hủy kết bạn thì bài <c>friends</c> biến mất ngay. Hàm
/// này được gọi lại ở MỖI lần đọc, không phải một lần lúc tạo bài.
///
/// Hàm này dùng cho <c>GET /posts/{postId}</c>. Endpoint DANH SÁCH cố ý <b>không</b> gọi nó trong vòng lặp mà lặp lại
/// cùng ba mệnh đề trong <c>WHERE</c> của câu truy vấn (xem <see cref="IPostStore.ListByAuthorAsync"/>): lọc sau khi đã
/// <c>Take(limit)</c> thì trang trả về ít hơn <c>limit</c> một cách ngẫu nhiên và FE tưởng đã hết dữ liệu. Hai bản của
/// cùng một luật là chỗ lệch được, nên <c>PostVisibilityTests</c> có một test đối chiếu hai bên trên cùng bộ dữ liệu.
/// </summary>
public static class PostVisibility
{
    /// <param name="areFriends">
    /// Kết quả <c>IFriendshipReader.AreFriendsAsync</c> — tra thật qua SocialGraph (Đ-4.3), đọc thẳng DB.
    /// </param>
    public static bool CanView(PostPrivacy privacy, Guid authorId, Guid actorId, bool areFriends) => privacy switch
    {
        PostPrivacy.Public => true,
        PostPrivacy.Private => authorId == actorId,

        // Tác giả đứng trước `areFriends`: AreFriendsAsync trả false khi hai id bằng nhau (bản thân mình không
        // phải "bạn" của mình), nên bỏ vế đầu là tác giả không xem được bài friends của chính mình.
        PostPrivacy.Friends => authorId == actorId || areFriends,

        // Giá trị enum lạ (cột DB bị sửa tay, hay enum thêm thành viên ở GĐ sau mà quên nhánh) → KHÔNG cho xem.
        // Mặc định đóng: thêm `friends-of-friends` ở GĐ nào đó mà quên sửa đây thì bài bị ẩn, không phải bị lộ.
        _ => false,
    };
}
