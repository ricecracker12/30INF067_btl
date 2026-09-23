namespace SocialApp.SharedKernel.Events;

/// <summary>
/// Cửa phát event duy nhất (Đ-6.2, Đ-6.4). Singleton — tiêm qua constructor ở bất kỳ vòng đời nào.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Gọi SAU <c>COMMIT</c>. Ghi vào hàng đợi rồi trả về ngay: không chờ handler, không ném khi hàng đợi đầy (event bị rơi và
    /// được đếm). <c>void</c> có chủ đích — có <c>Task</c> là có người <c>await</c>, và chờ handler là phá luật 2 của Đ-6.2.
    /// Không cần bọc try/catch.
    /// </summary>
    void Publish(IIntegrationEvent integrationEvent);
}
