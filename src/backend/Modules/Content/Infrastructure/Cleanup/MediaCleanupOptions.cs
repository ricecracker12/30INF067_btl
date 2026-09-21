namespace SocialApp.Modules.Content.Infrastructure.Cleanup;

/// <summary>
/// Công tắc của worker dọn rác media (Đ-2.13, C4). Chỉ <see cref="Enabled"/> là cấu hình; các ngưỡng còn lại là HẰNG SỐ
/// vì Đ-2.13 đã chốt con số và mỗi con số có lý do riêng — để cấu hình được là mời người sau "tinh chỉnh" ngưỡng 24 giờ
/// xuống 10 phút cho bằng hạn presign, và xóa ảnh của bài đang soạn.
///
/// Mặc định TẮT (Q-C2, chốt 2026-09-19): worker đăng ký trong <c>AddContentModule</c> nên chạy trong MỌI host, kể cả
/// <c>WebApplicationFactory</c> của test. Mặc định bật là gọi R2 thật từ CI không có khóa, hoặc xóa object trong lúc test
/// khác đang dùng. Staging bật tường minh: <c>Media__Cleanup__Enabled=true</c> trong <c>deploy/.env</c>. "Quên bật trên
/// staging" nhìn thấy được (bucket tích rác); "quên tắt trên CI" thì không.
/// </summary>
public sealed class MediaCleanupOptions
{
    public const string Section = "Media:Cleanup";

    public bool Enabled { get; init; }

    /// <summary>Chu kỳ quét. Lượt đầu chạy SAU một chu kỳ kể từ lúc khởi động, không chạy ngay.</summary>
    public const int IntervalMinutes = 60;

    /// <summary>
    /// Khóa Redis hết hạn TRƯỚC chu kỳ kế (3000 s &lt; 3600 s): instance chết giữa lượt thì khóa tự nhả, không kẹt tới
    /// vô hạn. Dài hơn chu kỳ là có lượt không bao giờ chạy được.
    /// </summary>
    public const int LockSeconds = 3000;

    /// <summary>
    /// Object dưới <c>posts/</c> cũ hơn ngưỡng này mà không có dòng <c>media_attachments</c> là mồ côi. 24 giờ, KHÔNG phải
    /// 10 phút bằng hạn presign: người dùng chọn ảnh rồi đi ăn cơm, quay lại bấm đăng vẫn phải được — chỉ cần URL còn hạn
    /// lúc PUT. Xóa theo hạn presign là xóa ảnh của bài đang soạn.
    /// </summary>
    public const int OrphanAgeHours = 24;

    /// <summary>Bài xóa mềm quá ngưỡng này thì object và dòng <c>media_attachments</c> của nó bị dọn (Đ-2.10).</summary>
    public const int DeletedPostAgeDays = 7;

    /// <summary>Tối đa object xử lý mỗi nhánh mỗi lượt — để một bucket lớn không giữ khóa suốt cả tiếng (Mục 7.5).</summary>
    public const int BatchSize = 1000;
}
