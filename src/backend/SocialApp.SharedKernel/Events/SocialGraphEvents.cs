namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Phát bởi SocialGraph (GĐ4) sau <c>COMMIT</c> của <c>POST /friends/requests</c>. GĐ6 tiêu thụ: thông báo
/// <c>friend_request</c> cho <paramref name="AddresseeId"/> (Đ-6.17).
/// </summary>
/// <param name="RequesterId">Người gửi lời mời (người gọi API).</param>
/// <param name="AddresseeId">Người được mời.</param>
public sealed record FriendRequestSent(Guid RequesterId, Guid AddresseeId) : IIntegrationEvent;

/// <summary>
/// Phát bởi SocialGraph (GĐ4) sau <c>COMMIT</c> của <c>POST /friends/requests/{userId}/accept</c>. GĐ6 tiêu thụ: thông báo
/// <c>friend_accepted</c> cho <paramref name="RequesterId"/> (Đ-6.17).
/// </summary>
/// <param name="RequesterId">Người đã GỬI lời mời — người nhận thông báo.</param>
/// <param name="AccepterId">Người bấm chấp nhận (người gọi API).</param>
public sealed record FriendRequestAccepted(Guid RequesterId, Guid AccepterId) : IIntegrationEvent;
