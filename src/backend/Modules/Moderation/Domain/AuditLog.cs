using System.Net;

namespace SocialApp.Modules.Moderation.Domain;

/// <summary>
/// Một dòng nhật ký kiểm toán (ENT-13, bảng <c>moderation.audit_logs</c>): ai, làm gì, trên cái gì, lúc nào, từ IP nào.
///
/// <b>Append-only do DB giữ</b> (Đ-6.15): trigger <c>trg_audit_logs_append_only</c> từ chối mọi <c>UPDATE</c>/<c>DELETE</c>, và
/// <c>trg_audit_logs_no_truncate</c> từ chối <c>TRUNCATE</c>. Cửa duy nhất là <c>DELETE</c> khi phiên đặt
/// <c>SET LOCAL socialapp.audit_purge = 'on'</c> — dành cho job xóa 12 tháng của GĐ8.
///
/// Mọi thuộc tính là <c>init</c>: không ai "sửa một dòng audit" được bằng C#; trigger là lưới cho SQL thô. Không có
/// <c>UpdatedAt</c> — <c>ModerationDbContext.StampUpdatedAt</c> tự bỏ qua entity không có cột đó.
///
/// <see cref="Metadata"/> KHÔNG BAO GIỜ chứa nội dung bài, bình luận hay tin nhắn — chỉ id, mã, và ghi chú của Moderator.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Khóa tăng dần — cũng là khóa keyset của <c>GET /admin/audit-logs</c> (<c>id DESC</c>).</summary>
    public long Id { get; init; }

    public Guid ActorId { get; init; }

    /// <summary>Một trong <c>SharedKernel.Audit.AuditActions</c>.</summary>
    public required string Action { get; init; }

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }

    /// <summary>JSON (<c>jsonb</c>) — khóa–giá trị do người ghi đặt.</summary>
    public string? Metadata { get; init; }

    /// <summary>IP của người thao tác, đọc sau <c>ForwardedHeaders</c>. Dữ liệu cá nhân — không vào log.</summary>
    public IPAddress? Ip { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
