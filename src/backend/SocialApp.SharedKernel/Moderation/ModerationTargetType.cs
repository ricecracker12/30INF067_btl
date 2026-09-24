namespace SocialApp.SharedKernel.Moderation;

/// <summary>
/// Loại đối tượng bị báo cáo / kiểm duyệt (Đ-6.12, Đ-6.17). Đặt ở <c>SharedKernel/Moderation/</c> ngay từ khối C0 vì
/// <c>ContentHidden</c> cần nó; C2 dựng <c>ModerationTarget</c> + <c>IModerationTargets</c> cạnh đây mà không phải dời enum.
/// </summary>
public enum ModerationTargetType
{
    Post,
    Comment,
    User,
}
