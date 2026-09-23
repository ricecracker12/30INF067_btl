namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Đánh dấu một event trong tiến trình (Đ-6.2, <c>docs/giai-doan-6/giai-doan-6.md</c>). Bốn luật, mỗi luật chặn một lỗi cụ thể:
/// <list type="number">
/// <item><b>Phát SAU <c>COMMIT</c></b> — sau <c>await tx.CommitAsync()</c>, không bao giờ bên trong khối <c>await using var tx</c>.
/// Phát trong transaction là thông báo cho một dòng có thể bị rollback một giây sau.</item>
/// <item><b><see cref="IEventPublisher.Publish"/> không chờ handler.</b> Handler chậm hay ném lỗi không làm request gốc chậm
/// hay 500.</item>
/// <item><b>Event chỉ mang id, enum, số, cờ</b> — không nội dung, không tên hiển thị (Đ-5.18). Người tiêu thụ tự hydrate.
/// <c>IntegrationEventShapeTests</c> (EVT-07) canh luật này.</item>
/// <item><b>Hàng đợi có giới hạn</b>: đầy thì rơi event + đếm <c>socialapp_events_dropped_total</c> + cảnh báo có ngưỡng,
/// không chặn người phát.</item>
/// </list>
/// Mọi event là <c>sealed record</c> ở <c>SharedKernel/Events/</c>. Bus dispatch theo <b>kiểu chính xác</b>
/// (<c>GetType()</c>), không theo kiểu cha. Đổi chữ ký một record đã lên <c>develop</c> = báo A (GĐ3), B (GĐ5) trước, chỉ-thêm
/// tham số có mặc định (Mục 9.4).
/// </summary>
public interface IIntegrationEvent;
