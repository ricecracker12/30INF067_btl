namespace SocialApp.Modules.SocialGraph.Application;

/// <summary>
/// Đ-4.4/Đ-4.15: chỉ SocialGraph xóa <c>sg:feed-sources:*</c>. Gọi SAU khi thay đổi quan hệ đã COMMIT, từ
/// <c>RelationshipService</c> — một chỗ, không rải trong controller. Redis lỗi thì nuốt + log: TTL 60s chặn trên
/// độ cũ, còn ném ra là thao tác kết bạn đã COMMIT mà người dùng nhận 500. Cùng lý do, người gọi KHÔNG truyền token
/// của request: client ngắt sau COMMIT thì việc xóa vẫn phải chạy.
/// </summary>
public interface IFeedSourceCache
{
    Task InvalidateAsync(Guid userId, Guid? otherUserId = null, CancellationToken ct = default);
}
