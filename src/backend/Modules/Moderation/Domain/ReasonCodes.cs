namespace SocialApp.Modules.Moderation.Domain;

/// <summary>
/// Lý do báo cáo chuẩn hóa — đúng năm giá trị của PTTK ENT-12, khớp <c>ck_reports_reason</c>.
///
/// Đ-6.12: CHECK, KHÔNG bảng tham chiếu. Năm giá trị cố định mà FE hiển thị bằng nhãn tiếng Việt thì một bảng chỉ thêm một
/// join. Thêm lý do mới = migration đổi CHECK + chỉ-thêm enum trong hợp đồng <c>moderation-v1.yaml</c>.
/// </summary>
public static class ReasonCodes
{
    public const string Spam = "spam";

    public const string Harassment = "harassment";

    public const string Nudity = "nudity";

    public const string Violence = "violence";

    /// <summary>Bắt buộc kèm <c>detail</c> — <c>ck_reports_other_detail</c>.</summary>
    public const string Other = "other";

    /// <summary>Năm giá trị hợp lệ — CHECK dựng từ mảng này.</summary>
    public static readonly string[] All = [Spam, Harassment, Nudity, Violence, Other];
}
