namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Đối tượng của một cảm xúc — khớp đúng <c>ck_reactions_target</c> (Mục 4):
/// <c>target_type IN ('post','comment')</c>. Là một phần của khóa chính ba cột (BR-05).
/// </summary>
public enum ReactionTargetType
{
    Post,
    Comment,
}
