using SocialApp.SharedKernel.Moderation;

namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Phát bởi Moderation (GĐ6) sau <c>COMMIT</c> của quyết định ẩn nội dung. GĐ6 tiêu thụ: thông báo <c>moderation</c> cho
/// <paramref name="AuthorId"/> — không lộ người báo cáo hay Moderator (Đ-6.17, B.10 #7).
/// </summary>
/// <param name="PostId">Bài chứa đối tượng khi đối tượng là bài hoặc bình luận; <c>null</c> khi là người dùng.</param>
/// <param name="ReasonCode">Mã lý do trong tập CỐ ĐỊNH của CHECK <c>reason_code</c> (Đ-6.12) — không phải chữ người dùng gõ.
/// Là <c>string</c> DUY NHẤT được phép trong một event (luật 3 Đ-6.2); <c>IntegrationEventShapeTests</c> khai ngoại lệ này
/// tường minh. Đừng lấy nó làm tiền lệ để thêm <c>string</c> khác.</param>
public sealed record ContentHidden(
    ModerationTargetType TargetType,
    Guid TargetId,
    Guid? PostId,
    Guid AuthorId,
    string ReasonCode) : IIntegrationEvent;
