using SocialApp.Modules.Messaging.Domain;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// Tầng 3 của Messaging — chỗ DUY NHẤT kiểm BR-06 "chỉ hai thành viên" (giai-doan-5.md Mục 6.2). Mọi cửa vào (REST và hub,
/// đọc lẫn ghi) đi qua <see cref="ResolveMemberAsync"/>; không controller hay hub method nào tự so <c>UserAId</c>.
///
/// Không có nhánh "if role == ADMIN": Admin short-circuit CHỈ ở tầng 2 (GĐ1 Mục 3.2) — Admin không đọc trộm tin nhắn.
/// "Không tồn tại" và "không phải thành viên" trả CÙNG một <see cref="Result.Forbidden"/> (403 <c>auth.forbidden</c>, TC-A04):
/// mã trạng thái không tiết lộ id hội thoại có thật hay không (quy ước 3b GĐ1).
/// </summary>
public sealed class ConversationAccess(IConversationStore store)
{
    public async Task<Result<Conversation>> ResolveMemberAsync(Guid conversationId, Guid actorId, CancellationToken ct)
    {
        var conversation = await store.FindAsync(conversationId, ct);
        if (conversation is null || !conversation.IsMember(actorId))
            return Result<Conversation>.Forbidden();

        return conversation;
    }
}
