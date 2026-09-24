using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Messaging.Application;

/// <summary>
/// Mọi <see cref="Error"/> của module Messaging ở MỘT chỗ (khuôn <c>SocialGraphErrors</c>): rà PII bằng một lần đọc — không thông
/// điệp nào chứa id, nội dung tin hay tên kiểu .NET (DoD Mục 11). Thông điệp chép ĐÚNG câu trong <c>example</c> của
/// <c>messaging-v1.yaml</c>; FE hiện câu của server (luật frontend Mục 6).
///
/// 403 "không phải thành viên / không tồn tại / thiếu quyền" dùng <see cref="Error.Forbidden"/> của SharedKernel (Mục 6.1).
/// </summary>
public static class MessagingErrors
{
    /// <summary>
    /// 503 của <c>POST /realtime/tickets</c> khi Redis không sẵn sàng (Đ-5.9) — không fail-open: không có chỗ lưu thì không có vé
    /// để kiểm. Client nhận 503 thì chuyển fallback REST (Đ-5.12). <c>Type</c> riêng để FE phân nhánh theo <c>type</c>, không
    /// theo status (luật frontend Mục 4): 503 này khác 503 "feed quá tải" và 503 "BFF mất kho phiên".
    /// </summary>
    public static readonly Error RealtimeUnavailable = new(
        "messaging.realtime-unavailable",
        "Kênh thời gian thực tạm thời không sẵn sàng. Tin nhắn vẫn gửi được.",
        503,
        Title: "Kênh thời gian thực không sẵn sàng",   // bảng mặc định gộp >= 500 vào "lỗi không mong muốn" — sai nghĩa với 503
        Type: RealtimeUnavailableType);

    /// <summary>Theo quy ước <c>urn:socialapp:problem:*</c> của <c>ContentErrors.FeedOverloadedType</c> — khai trong hợp đồng.</summary>
    public const string RealtimeUnavailableType = "urn:socialapp:problem:realtime-unavailable";
}
