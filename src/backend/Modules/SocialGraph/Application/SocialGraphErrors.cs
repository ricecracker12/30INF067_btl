using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.SocialGraph.Application;

/// <summary>
/// Mọi <see cref="Error"/> của module SocialGraph ở MỘT chỗ — cùng lý do với <c>ContentErrors</c> của GĐ2: rà PII bằng
/// một lần đọc (không thông điệp nào chứa userId hay tên kiểu .NET), và một loại lỗi chỉ có đúng một cách nói.
///
/// Thông điệp chép ĐÚNG câu trong <c>example</c> của <c>socialgraph-v1.yaml</c>. Luật frontend Mục 6 bắt FE hiện câu
/// của server; sửa câu ở đây mà không sửa yaml là hai nguồn lệch nhau.
///
/// Không có lỗi 403 nào trong danh sách này: mọi 403 của SocialGraph (thiếu quyền, không có lời mời, tự chấp nhận,
/// người thứ ba) dùng <see cref="Error.Forbidden"/> của SharedKernel — hợp đồng đã chốt "cùng một phản hồi, không
/// nêu lý do nào" (Đ-4.14).
/// </summary>
public static class SocialGraphErrors
{
    /// <summary>
    /// 400 của <c>POST /friends/requests</c> khi <c>userId</c> là chính người gọi. Kiểm <b>trước</b> DB (Đ-4.14).
    /// </summary>
    public static Error SelfRequest =>
        Error.Validation("userId", "Không thể gửi lời mời kết bạn cho chính mình.");

    /// <summary>
    /// 400 của <c>POST /friends/requests/{userId}/accept</c> khi <c>userId</c> là chính người gọi.
    /// Kiểm <b>trước</b> DB: <c>FriendPair.Of</c> ném nếu lọt. Khác
    /// <c>TC-A03-friend-self-accept</c> (A gửi cho B rồi A gọi accept với id của B → 403, 0 dòng).
    /// </summary>
    public static Error SelfAccept =>
        Error.Validation("userId", "Không thể chấp nhận lời mời kết bạn với chính mình.");

    /// <summary>
    /// 400 của <c>DELETE /friends/requests/{userId}</c> khi <c>userId</c> là chính người gọi.
    /// Kiểm <b>trước</b> DB: <c>FriendPair.Of</c> ném nếu lọt. Yaml không có example riêng — cùng key
    /// <c>userId</c> với các lỗi tự-thao-tác kia.
    /// </summary>
    public static Error SelfDecline =>
        Error.Validation("userId", "Không thể hủy lời mời kết bạn với chính mình.");

    /// <summary>
    /// 400 của <c>DELETE /friends/{userId}</c> khi <c>userId</c> là chính người gọi. Kiểm trước DB.
    /// </summary>
    public static Error SelfUnfriend =>
        Error.Validation("userId", "Không thể hủy kết bạn với chính mình.");

    /// <summary>
    /// 400 của <c>PUT /follows/{userId}</c> khi <c>userId</c> là chính người gọi. Kiểm trước DB.
    /// </summary>
    public static Error SelfFollow =>
        Error.Validation("userId", "Không thể theo dõi chính mình.");

    /// <summary>
    /// 400 của <c>GET /relationships/{userId}</c> khi hỏi quan hệ với chính mình. Hợp đồng không có example riêng —
    /// cùng key <c>userId</c> với hai lỗi tự-thao-tác kia.
    /// </summary>
    public static Error SelfRelationship =>
        Error.Validation("userId", "Không thể xem quan hệ với chính mình.");

    /// <summary>
    /// 404 khi người được mời / được theo dõi không có hồ sơ (Đ-2.4). Chỉ cho thao tác GHI cần đối phương tồn tại;
    /// <c>GET /relationships/{userId}</c> với người không tồn tại trả 200 <c>none</c>/<c>false</c>, không dùng lỗi này.
    /// </summary>
    public static readonly Error UserNotFound = new("sg.user_not_found", "Không tìm thấy người dùng.", 404);

    /// <summary>
    /// 409 của <c>POST /friends/requests</c> khi đụng PK <c>friendships</c> (đã có lời mời theo bất kỳ chiều nào, hoặc
    /// đã là bạn). Store bắt <c>PostgresException 23505</c> rồi trả lỗi này — không để exception chảy lên thành 500.
    /// </summary>
    public static readonly Error RelationshipExists =
        new("sg.relationship_exists", "Đã có lời mời hoặc quan hệ bạn bè giữa hai người.", 409);
}
