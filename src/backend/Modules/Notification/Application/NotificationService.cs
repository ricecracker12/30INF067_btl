using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Notification.Application;

/// <summary>Một dòng <c>notification.notifications</c> như DB trả.</summary>
public sealed record NotificationRow(
    Guid Id,
    string Type,
    string TargetType,
    Guid TargetId,
    Guid? PostId,
    Guid? LastActorId,
    int ActorCount,
    string? ReasonCode,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Đường ĐỌC và ĐÁNH DẤU ĐÃ ĐỌC của thông báo (GĐ6 D11). Đường ghi sự kiện ở <see cref="INotificationStore"/> (D9). Mọi hàm lọc theo
/// <c>recipient_id</c> của NGƯỜI GỌI — không hàm nào nhận id thông báo mà không nhận người nhận (tầng 3).
///
/// Hai hàm đánh dấu CHỈ đặt <c>is_read</c>, không bao giờ chạm <c>updated_at</c>: đó là khóa sắp danh sách, đổi nó thì đọc một thông báo
/// cũ là nó nhảy lên đầu (bài học A2, cạm bẫy 1).
/// </summary>
public interface INotificationQueries
{
    /// <param name="take">Người gọi truyền <c>limit + 1</c> để biết còn trang sau.</param>
    Task<IReadOnlyList<NotificationRow>> ListAsync(Guid recipientId, NotificationCursor? after, int take, CancellationToken ct);

    Task<int> CountUnreadAsync(Guid recipientId, CancellationToken ct);

    /// <returns><c>false</c> khi không dòng nào khớp <c>(id, recipient_id)</c> — không tồn tại HOẶC không phải của người gọi.</returns>
    Task<bool> MarkReadAsync(Guid recipientId, Guid notificationId, CancellationToken ct);

    Task MarkAllReadAsync(Guid recipientId, DateTimeOffset upTo, CancellationToken ct);
}

/// <summary>
/// Bốn endpoint của <c>notification-v1</c> (GĐ6 D11, Mục 8.3). Không mã quyền nào (Mục 6.1 — cùng lý do <c>/me</c>): mọi người đăng nhập
/// đều có thông báo của mình, tầng 3 là "chỉ của mình".
///
/// Số câu SQL mỗi trang không phụ thuộc số nhóm (<c>NOTIF-08</c>): một câu thông báo + một lô <c>IUserDirectory</c> cho mọi người khác
/// nhau của trang.
/// </summary>
public sealed class NotificationService(INotificationQueries queries, IUserDirectory directory, IObjectStorage storage)
{
    /// <summary>Gọi SAU validator.</summary>
    public async Task<NotificationPage> ListAsync(Guid recipientId, ListNotificationsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        NotificationCursor? after = NotificationCursor.TryDecode(query.Cursor, out var cursor) ? cursor : null;
        var limit = query.EffectiveLimit;
        var rows = await queries.ListAsync(recipientId, after, limit + 1, ct);

        var page = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        var next = rows.Count > limit ? new NotificationCursor(page[^1].UpdatedAt, page[^1].Id).Encode() : null;
        if (page.Count == 0)
            return new NotificationPage([], next);

        var actorIds = page.Where(r => r.LastActorId is not null).Select(r => r.LastActorId!.Value).Distinct().ToList();
        var cards = actorIds.Count == 0
            ? new Dictionary<Guid, UserCard>()
            : await directory.GetManyAsync(actorIds, ct);

        return new NotificationPage([.. page.Select(r => ToResponse(r, cards))], next);
    }

    public async Task<UnreadNotificationCount> CountUnreadAsync(Guid recipientId, CancellationToken ct) =>
        new(await queries.CountUnreadAsync(recipientId, ct));

    /// <summary>
    /// 403 cho CẢ "không tồn tại" lẫn "của người khác" (quy ước 3b, <c>NOTIF-IDOR</c>): 404 cho một và 403 cho cái kia là để status code tố
    /// cáo id nào có thật. Đã đọc rồi vẫn khớp một dòng → 204 (idempotent).
    /// </summary>
    public async Task<Result> MarkReadAsync(Guid recipientId, Guid notificationId, CancellationToken ct) =>
        await queries.MarkReadAsync(recipientId, notificationId, ct) ? Result.Success() : Result.Forbidden();

    /// <summary>Gọi SAU validator (<c>upTo</c> đã có).</summary>
    public Task MarkAllReadAsync(Guid recipientId, ReadAllRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return queries.MarkAllReadAsync(recipientId, request.UpTo!.Value.ToUniversalTime(), ct);
    }

    private NotificationResponse ToResponse(NotificationRow row, IReadOnlyDictionary<Guid, UserCard> cards) => new(
        row.Id,
        row.Type,
        row.LastActorId is { } actor && cards.TryGetValue(actor, out var card)
            ? new NotificationUserCard(
                card.UserId, card.DisplayName, card.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null)
            : null,
        row.ActorCount,
        new NotificationTarget(row.TargetType, row.TargetId, row.PostId),
        row.ReasonCode,
        row.IsRead,
        row.CreatedAt,
        row.UpdatedAt);
}
