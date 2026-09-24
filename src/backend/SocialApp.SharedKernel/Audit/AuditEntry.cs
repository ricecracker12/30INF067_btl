namespace SocialApp.SharedKernel.Audit;

/// <summary>
/// Một dòng nhật ký kiểm toán như người gọi mô tả nó (Đ-6.15). IP và thời điểm KHÔNG ở đây — hiện thực tự điền.
///
/// <see cref="Metadata"/> là khóa–giá trị, không phải <c>object</c> tự do: người gọi không có đường nào đẩy nguyên một entity (có
/// <c>Body</c>) vào audit. Giá trị chỉ là id, mã, cờ, và ghi chú của Moderator — KHÔNG BAO GIỜ nội dung bài, bình luận, tin nhắn,
/// email. Khóa viết camelCase như bảng Đ-6.15 (<c>reportIds</c>, <c>fromRole</c>, <c>routeTemplate</c>).
/// </summary>
/// <param name="ActorId">Người thao tác — lấy từ token (<c>GetUserId()</c>), không bao giờ từ route hay body (B.10 tự rà #4).</param>
/// <param name="Action">Một trong <see cref="AuditActions"/>.</param>
/// <param name="TargetType"><c>post</c> · <c>comment</c> · <c>user</c> · <c>role</c> · <c>endpoint</c>; null khi không có đối tượng.</param>
/// <param name="TargetId">Id đối tượng; null với <c>endpoint</c> (không id trên đường).</param>
/// <param name="Metadata">Khóa–giá trị, serialize thành <c>jsonb</c>.</param>
public sealed record AuditEntry(
    Guid ActorId,
    string Action,
    string? TargetType,
    Guid? TargetId,
    IReadOnlyDictionary<string, object?>? Metadata = null);
