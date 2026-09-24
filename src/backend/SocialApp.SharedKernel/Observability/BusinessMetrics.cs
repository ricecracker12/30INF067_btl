using Prometheus;
using SocialApp.SharedKernel.Events;

namespace SocialApp.SharedKernel.Observability;

/// <summary>
/// Bốn chỉ số nghiệp vụ của GĐ7 (giai-doan-7.md Mục 5.2) và hai counter của event bus GĐ6 (Đ-6.2), khai báo ở MỘT chỗ để tên
/// chỉ số không trôi giữa các module. Host xuất chúng ra <c>/metrics</c> cùng RED metrics (C1) — cùng registry mặc định của
/// prometheus-net. Chỉ số mới của GĐ6 trở đi cũng khai ở đây: <c>System.Diagnostics.Metrics</c> không lên <c>/metrics</c>
/// (GĐ7 C2 thử bốn cách; event bus C0 đếm theo cách đó và vô hình tới khi chuyển về đây).
///
/// Module gọi các phương thức tĩnh bên dưới, <b>không</b> chạm kiểu <c>Counter</c> của Prometheus: code nghiệp vụ không
/// dính vào thư viện đo lường, và đổi thư viện sau này chỉ sửa file này.
///
/// <b>Không</b> đưa id người dùng, email, key hay bất kỳ giá trị nào có số lượng không giới hạn vào nhãn: mỗi giá trị nhãn
/// là một chuỗi thời gian riêng trong Prometheus, và nhãn là dữ liệu ai đọc được <c>/metrics</c> cũng thấy.
/// </summary>
public static class BusinessMetrics
{
    private static readonly Counter LoginFailedCounter = Metrics.CreateCounter(
        "socialapp_login_failed_total",
        "Lần đăng nhập bị từ chối vì sai thông tin (401): email không tồn tại hoặc sai mật khẩu. Tăng đột biến = đang bị dò mật khẩu (ISS-04).");

    private static readonly Counter PostsCreatedCounter = Metrics.CreateCounter(
        "socialapp_posts_created_total",
        "Bài đăng tạo thành công. Nằm yên ở 0 quá lâu = hỏng ở đâu đó dù /health/ready vẫn xanh.");

    private static readonly Counter PresignIssuedCounter = Metrics.CreateCounter(
        "socialapp_presign_issued_total",
        "URL tải lên đã ký, theo mục đích. Đối chiếu với số object trên R2 để thấy rác upload dở (Đ-2.13).",
        new CounterConfiguration { LabelNames = ["purpose"] });

    private static readonly Counter MediaCleanupRunsCounter = Metrics.CreateCounter(
        "socialapp_media_cleanup_runs_total",
        "Lượt worker dọn rác media, theo kết quả: ran | lock | failed. Chứng minh worker thật sự chạy, kể cả khi không có gì để dọn.",
        new CounterConfiguration { LabelNames = ["result"] });

    // GĐ5 (giai-doan-5.md D5, D7, Mục 10.7): chat. Nhãn `channel` chỉ có hai giá trị cố định — không id, không nội dung (Đ-5.18).
    private static readonly Counter MessagesSentCounter = Metrics.CreateCounter(
        "socialapp_messages_sent_total",
        "Tin nhắn MỚI đã COMMIT, theo cửa vào: hub | rest. Gửi lại cùng clientMsgId (replayed) không đếm. rest tăng vọt = WebSocket đang hỏng (fallback Đ-5.12).",
        new CounterConfiguration { LabelNames = ["channel"] });

    private static readonly Histogram MessagePushSeconds = Metrics.CreateHistogram(
        "socialapp_message_push_seconds",
        "Từ COMMIT của tin tới lúc gọi xong SendAsync của hub (phần server của p95 gửi→nhận, GOAL-02). Khi p95 không đạt, tách được chậm ở server hay ở đường truyền.",
        new HistogramConfiguration { Buckets = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1] });
    // Nhãn `event` = tên kiểu record trong SharedKernel/Events — tập đóng, sáu giá trị, không phải dữ liệu người dùng.
    private static readonly Counter EventsPublishedCounter = Metrics.CreateCounter(
        "socialapp_events_published_total",
        "Event đã nhận vào hàng đợi của event bus trong tiến trình, kể cả khi chưa có handler (Đ-6.2).",
        new CounterConfiguration { LabelNames = ["event"] });

    private static readonly Counter EventsDroppedCounter = Metrics.CreateCounter(
        "socialapp_events_dropped_total",
        "Event bị rơi vì hàng đợi event bus đầy. Lớn hơn 0 = handler chậm chặn hàng đợi, thông báo đã mất (R6-10).",
        new CounterConfiguration { LabelNames = ["event"] });

    // GĐ6 D3 (Đ-6.6): không nhãn — số lần, không phải của ai. Id tài khoản nằm trong log Error cùng lúc, không ở đây.
    private static readonly Counter RevocationFailuresCounter = Metrics.CreateCounter(
        "socialapp_revocation_failures_total",
        "Lần ghi mốc thu hồi revoked:user hỏng sau 3 lần thử, SAU khi DB đã đổi (khóa tài khoản, đổi vai trò). Lớn hơn 0 = có phiên giữ quyền cũ tới 15 phút (Đ-6.6).");

    /// <summary>
    /// Host gọi MỘT lần lúc khởi động. Không có lời gọi này thì <c>/metrics</c> <b>trống</b> các chỉ số trên cho tới sự
    /// kiện đầu tiên: field static của lớp chỉ khởi tạo khi có ai chạm vào lớp, và nhãn chỉ thành chuỗi thời gian khi có
    /// giá trị đầu tiên (đã gặp trên staging 2026-09-23 — deploy xong, <c>grep socialapp_</c> ra rỗng). Hậu quả không
    /// chỉ là "chưa thấy": <c>increase()</c> mất luôn lần tăng đầu tiên sau mỗi lần deploy, và cảnh báo "đứng yên ở 0"
    /// không kêu được trên một chuỗi không tồn tại. Nên ở đây tạo sẵn cả mười chuỗi đếm (bảy của GĐ7, hai kênh gửi tin của GĐ5,
    /// thu hồi của GĐ6) và histogram đẩy tin với giá trị 0 — cùng hai counter event bus cho mọi kiểu event của SharedKernel
    /// (tìm bằng phản chiếu: thêm record event mới là tự có chuỗi, không sửa ở đây).
    /// </summary>
    public static void Initialize()
    {
        _ = LoginFailedCounter;
        _ = PostsCreatedCounter;
        _ = RevocationFailuresCounter;
        foreach (var purpose in (string[])["post", "avatar"])
            PresignIssuedCounter.WithLabels(purpose);
        foreach (var result in (string[])["ran", "lock", "failed"])
            MediaCleanupRunsCounter.WithLabels(result);
        foreach (var channel in (string[])["hub", "rest"])
            MessagesSentCounter.WithLabels(channel);
        _ = MessagePushSeconds;
        foreach (var eventType in typeof(IIntegrationEvent).Assembly.GetTypes()
                     .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false }))
        {
            EventsPublishedCounter.WithLabels(eventType.Name);
            EventsDroppedCounter.WithLabels(eventType.Name);
        }
    }

    /// <summary>Đăng nhập trả 401 vì sai thông tin — gọi ở CẢ nhánh email không tồn tại lẫn nhánh sai mật khẩu.</summary>
    public static void LoginFailed() => LoginFailedCounter.Inc();

    /// <summary>Bài đã ghi xong vào DB. Gọi SAU khi lưu thành công, không gọi ở nhánh bị từ chối.</summary>
    public static void PostCreated() => PostsCreatedCounter.Inc();

    /// <summary><paramref name="count"/> URL tải lên vừa ký cho <paramref name="purpose"/> (<c>post</c> | <c>avatar</c>).</summary>
    public static void PresignIssued(string purpose, int count) => PresignIssuedCounter.WithLabels(purpose).Inc(count);

    /// <summary>Một lượt worker dọn rác kết thúc với <paramref name="result"/> (<c>ran</c> | <c>lock</c> | <c>failed</c>).</summary>
    public static void MediaCleanupRun(string result) => MediaCleanupRunsCounter.WithLabels(result).Inc();

    /// <summary>Một tin MỚI đã COMMIT qua <paramref name="channel"/> (<c>hub</c> | <c>rest</c>). Không gọi khi gửi lại (replayed).</summary>
    public static void MessageSent(string channel) => MessagesSentCounter.WithLabels(channel).Inc();

    /// <summary>Thời gian từ COMMIT tới khi đẩy xong <c>MessageReceived</c>, tính bằng giây.</summary>
    public static void MessagePushed(double seconds) => MessagePushSeconds.Observe(seconds);
    /// <summary>
    /// Ghi <c>revoked:user</c> hỏng hẳn sau 3 lần thử (Đ-6.6) — người gọi đã log Error và trả <c>revocation: deferred</c>.
    /// </summary>
    public static void RevocationFailed() => RevocationFailuresCounter.Inc();

    /// <summary>Event bus vừa nhận <paramref name="eventType"/> vào hàng đợi — gọi ở <c>Publish</c>, trước khi biết có rơi hay không.</summary>
    public static void EventPublished(Type eventType) => EventsPublishedCounter.WithLabels(eventType.Name).Inc();

    /// <summary>Event bus rơi <paramref name="eventType"/> vì hàng đợi đầy.</summary>
    public static void EventDropped(Type eventType) => EventsDroppedCounter.WithLabels(eventType.Name).Inc();
}
