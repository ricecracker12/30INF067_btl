using Prometheus;

namespace SocialApp.SharedKernel.Observability;

/// <summary>
/// Bốn chỉ số nghiệp vụ của GĐ7 (giai-doan-7.md Mục 5.2), khai báo ở MỘT chỗ để tên chỉ số không trôi giữa các module.
/// Host xuất chúng ra <c>/metrics</c> cùng RED metrics (C1) — cùng registry mặc định của prometheus-net.
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

    /// <summary>
    /// Host gọi MỘT lần lúc khởi động. Không có lời gọi này thì <c>/metrics</c> <b>trống</b> các chỉ số trên cho tới sự
    /// kiện đầu tiên: field static của lớp chỉ khởi tạo khi có ai chạm vào lớp, và nhãn chỉ thành chuỗi thời gian khi có
    /// giá trị đầu tiên (đã gặp trên staging 2026-09-23 — deploy xong, <c>grep socialapp_</c> ra rỗng). Hậu quả không
    /// chỉ là "chưa thấy": <c>increase()</c> mất luôn lần tăng đầu tiên sau mỗi lần deploy, và cảnh báo "đứng yên ở 0"
    /// không kêu được trên một chuỗi không tồn tại. Nên ở đây tạo sẵn cả bảy chuỗi với giá trị 0.
    /// </summary>
    public static void Initialize()
    {
        _ = LoginFailedCounter;
        _ = PostsCreatedCounter;
        foreach (var purpose in (string[])["post", "avatar"])
            PresignIssuedCounter.WithLabels(purpose);
        foreach (var result in (string[])["ran", "lock", "failed"])
            MediaCleanupRunsCounter.WithLabels(result);
    }

    /// <summary>Đăng nhập trả 401 vì sai thông tin — gọi ở CẢ nhánh email không tồn tại lẫn nhánh sai mật khẩu.</summary>
    public static void LoginFailed() => LoginFailedCounter.Inc();

    /// <summary>Bài đã ghi xong vào DB. Gọi SAU khi lưu thành công, không gọi ở nhánh bị từ chối.</summary>
    public static void PostCreated() => PostsCreatedCounter.Inc();

    /// <summary><paramref name="count"/> URL tải lên vừa ký cho <paramref name="purpose"/> (<c>post</c> | <c>avatar</c>).</summary>
    public static void PresignIssued(string purpose, int count) => PresignIssuedCounter.WithLabels(purpose).Inc(count);

    /// <summary>Một lượt worker dọn rác kết thúc với <paramref name="result"/> (<c>ran</c> | <c>lock</c> | <c>failed</c>).</summary>
    public static void MediaCleanupRun(string result) => MediaCleanupRunsCounter.WithLabels(result).Inc();
}
