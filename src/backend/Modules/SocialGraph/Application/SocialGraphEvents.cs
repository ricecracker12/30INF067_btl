using Microsoft.Extensions.Logging;

namespace SocialApp.Modules.SocialGraph.Application;

/// <summary>
/// Event trong tiến trình sau <c>COMMIT</c> (Đ-4.15). GĐ4 <b>chỉ log</b>; GĐ6 thay thân hàm để nối notification
/// (UC-10 bước 2, 4). Chữ ký hai phương thức giữ nguyên từ bây giờ để GĐ6 không phải sửa chỗ gọi ở
/// <c>RelationshipService</c>.
///
/// Log mức Information <b>không</b> kèm id người dùng — id là PII. Hai tham số vẫn có mặt vì GĐ6 cần chúng.
/// </summary>
public sealed class SocialGraphEvents(ILogger<SocialGraphEvents> logger)
{
    /// <summary>A đã gửi lời mời cho B. Gọi sau <c>COMMIT</c> của <c>POST /friends/requests</c> (D2).</summary>
    public void FriendRequestSent(Guid a, Guid b)
    {
        _ = (a, b);
        logger.LogInformation("Đã gửi lời mời kết bạn");
    }

    /// <summary>B đã chấp nhận lời mời của A. Gọi sau <c>COMMIT</c> của accept (D3).</summary>
    public void FriendRequestAccepted(Guid a, Guid b)
    {
        _ = (a, b);
        logger.LogInformation("Đã chấp nhận lời mời kết bạn");
    }
}
