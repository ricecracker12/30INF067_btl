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

    /// <summary>
    /// 403 khi hai người không (còn) là bạn — BR-09: mở hội thoại mới, hoặc gửi tin trong hội thoại cũ sau khi hủy kết bạn
    /// (AC-04). KHÁC <see cref="Error.Forbidden"/> có chủ đích (Mục 6.1): người nhận lỗi này là THÀNH VIÊN (gửi tin) hoặc đang
    /// mở hội thoại với một người có thật — cho họ biết "không còn là bạn" không lộ gì, và FE cần nó để hiện thanh "chỉ đọc".
    /// FE phân nhánh theo <c>type</c>.
    /// </summary>
    public static readonly Error NotFriends = new(
        "messaging.not-friends",
        "Hai bạn không còn là bạn bè. Hội thoại chỉ đọc.",
        403,
        Title: "Không phải bạn bè",
        Type: NotFriendsType);

    public const string NotFriendsType = "urn:socialapp:problem:not-friends";

    /// <summary>400 của <c>POST /conversations</c> khi <c>userId</c> là chính người gọi (Mục 8.1). Kiểm TRƯỚC DB.</summary>
    public static Error SelfConversation => Error.Validation("userId", "Không thể nhắn tin cho chính mình.");

    /// <summary>404 khi người kia không có hồ sơ (Đ-2.4 — người chưa onboarding không tồn tại với phần còn lại của hệ thống).</summary>
    public static readonly Error UserNotFound = new("messaging.user-not-found", "Không tìm thấy người dùng.", 404);

    /// <summary>
    /// 409 khi <c>clientMsgId</c> đã dùng cho một nội dung KHÁC (Đ-5.5) — bug của client, phải lộ ra. Gửi lại cùng nội dung
    /// KHÔNG phải lỗi này (200 + tin cũ).
    /// </summary>
    public static readonly Error ClientMsgIdReused = new(
        "messaging.client-msg-id-reused",
        "Mã tin phía client đã được dùng cho một tin khác.",
        409);
}
