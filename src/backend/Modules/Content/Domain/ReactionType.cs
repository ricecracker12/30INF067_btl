namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Sáu loại cảm xúc — khớp đúng <c>ck_reactions_type</c> (Mục 4):
/// <c>type IN ('like','love','haha','wow','sad','angry')</c>.
///
/// Khung của Đ-2.12: GĐ2 dựng bảng và ràng buộc, không có endpoint nào. Khóa jsonb của
/// <c>posts.reaction_counts</c> chính là các tên này ở dạng chữ thường.
/// </summary>
public enum ReactionType
{
    Like,
    Love,
    Haha,
    Wow,
    Sad,
    Angry,
}
