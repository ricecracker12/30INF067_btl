using SocialApp.SharedKernel.Events;

namespace SocialApp.Modules.SocialGraph.Application;

/// <summary>
/// Event trong tiến trình sau <c>COMMIT</c> (Đ-4.15), phát qua <see cref="IEventPublisher"/> (Đ-6.2, Đ-6.4) — GĐ6 nối thông báo
/// <c>friend_request</c>, <c>friend_accepted</c> vào đây (UC-10 bước 2, 4). Chữ ký hai phương thức giữ nguyên từ GĐ4 nên chỗ gọi
/// ở <c>RelationshipService</c> không đổi.
///
/// Không log gì: <c>Publish</c> không chờ handler và không ném, bus tự đếm metric. Tên tham số nói VAI của từng id — đổi chỗ
/// hai id là thông báo gửi nhầm người mà compile vẫn được (<c>SocialGraphEventsTests</c>, EVT-06).
/// </summary>
public sealed class SocialGraphEvents(IEventPublisher publisher)
{
    /// <summary>A đã gửi lời mời cho B. Gọi sau <c>COMMIT</c> của <c>POST /friends/requests</c> (D2).</summary>
    public void FriendRequestSent(Guid requesterId, Guid addresseeId) =>
        publisher.Publish(new SharedKernel.Events.FriendRequestSent(requesterId, addresseeId));

    /// <summary>B đã chấp nhận lời mời của A. Gọi sau <c>COMMIT</c> của accept (D3).</summary>
    /// <param name="requesterId">Người đã GỬI lời mời (A) — người nhận thông báo.</param>
    /// <param name="accepterId">Người bấm chấp nhận (B, người gọi API).</param>
    public void FriendRequestAccepted(Guid requesterId, Guid accepterId) =>
        publisher.Publish(new SharedKernel.Events.FriendRequestAccepted(requesterId, accepterId));
}
