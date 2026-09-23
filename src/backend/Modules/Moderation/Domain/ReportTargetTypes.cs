using SocialApp.SharedKernel.Moderation;

namespace SocialApp.Modules.Moderation.Domain;

/// <summary>
/// Loại đối tượng bị báo cáo dưới dạng chuỗi DB/hợp đồng — khớp <c>ck_reports_target_type</c>.
///
/// Enum dùng giữa các module là <see cref="ModerationTargetType"/> ở SharedKernel (C0); lớp này chỉ là phép dịch enum ↔ chuỗi
/// cột <c>target_type</c>, đặt ở MỘT chỗ để ba nơi đọc/ghi cột không mỗi nơi tự viết thường tên enum.
/// </summary>
public static class ReportTargetTypes
{
    public const string Post = "post";

    public const string Comment = "comment";

    public const string User = "user";

    /// <summary>Ba giá trị hợp lệ — CHECK dựng từ mảng này.</summary>
    public static readonly string[] All = [Post, Comment, User];

    public static string From(ModerationTargetType type) => type switch
    {
        ModerationTargetType.Post => Post,
        ModerationTargetType.Comment => Comment,
        ModerationTargetType.User => User,
        // Thêm thành viên enum mà quên nhánh → ném, không ghi một chuỗi lạ mà CHECK sẽ chặn muộn hơn với lỗi khó đọc.
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Loại đối tượng kiểm duyệt chưa có chuỗi DB."),
    };
}
