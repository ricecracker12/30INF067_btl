using System.Data.Common;

namespace SocialApp.SharedKernel.Audit;

/// <summary>
/// Ghi nhật ký kiểm toán (ENT-13) TRONG transaction của người gọi — hợp đồng GHI thứ nhất trong hai hợp đồng ghi của dự án.
///
/// <b>Lệch Đ-2.3 luật 1 có chủ đích</b> (Đ-6.3 của giai-doan-6.md): hợp đồng ở SharedKernel vốn chỉ đọc. PTTK đòi "thao tác +
/// audit cùng transaction" (ENT-12, UC-19) xuyên hai module; truyền <see cref="DbTransaction"/> của người gọi là cách duy nhất có
/// một transaction Postgres thật mà Moderation vẫn là module DUY NHẤT viết SQL vào <c>moderation.audit_logs</c>. Đúng HAI hợp đồng
/// ghi được phép — cái này và <c>IModerationTargets</c>; thêm cái thứ ba là một quyết định mới, có ngày.
///
/// Hiện thực ở Moderation (<c>SqlAuditTrail</c>). Chỉ THÊM — không sửa, không xóa, không đọc (đọc là <c>GET /admin/audit-logs</c>
/// của Moderation, D8).
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Thêm một dòng audit.
    /// <list type="bullet">
    /// <item><paramref name="tx"/> khác null → INSERT trên ĐÚNG kết nối + transaction đó: thao tác rollback thì dòng audit rollback
    /// theo, thao tác commit thì dòng audit chắc chắn có (mốc 3 của GĐ6). Lỗi ghi audit NÉM ra — người gọi để nó làm cả thao tác
    /// rollback, không nuốt.</item>
    /// <item><paramref name="tx"/> null → tự ghi trên kết nối riêng. CHỈ cho <see cref="AuditActions.AccessDenied"/> (Đ-6.15) — lúc
    /// bị từ chối không có thao tác nào để chung số phận.</item>
    /// </list>
    /// IP người thao tác do hiện thực tự lấy từ request hiện tại — người gọi không truyền.
    /// </summary>
    Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct = default);
}
