namespace SocialApp.Modules.Identity.Application.Roles;

/// <summary>
/// Chênh lệch giữa tập quyền hiện tại của một vai trò và tập được yêu cầu (GĐ6 D5, Đ-6.9) — hàm thuần, một chỗ cho ba người dùng:
/// 409 <c>confirmation-required</c> (<c>added</c>, <c>removed</c> để FE hiện đúng con số), luật "vai trò hệ thống không về 0 quyền",
/// và metadata audit <c>role.permissions</c>.
///
/// Hai danh sách sắp <see cref="StringComparer.Ordinal"/>: FE và test so được, audit đọc được, không phụ thuộc thứ tự request.
/// </summary>
public sealed record RolePermissionDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, bool IsEmptyAfter)
{
    /// <summary>Có gì để ghi không — <c>false</c> thì PUT là no-op: 200, không audit, không phát invalidate (L-D10).</summary>
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;

    /// <summary>
    /// <paramref name="requested"/> là tập (mã trùng trong request đã gộp ở người gọi hoặc ở đây — tập thì không trùng). So phân biệt
    /// hoa thường: mã quyền là hằng <c>resource.action</c> chữ thường, <c>POST.CREATE</c> là một mã khác (validator đã chặn).
    /// </summary>
    public static RolePermissionDiff Compute(IReadOnlySet<string> current, IReadOnlySet<string> requested)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(requested);

        var added = requested.Where(c => !current.Contains(c)).Order(StringComparer.Ordinal).ToList();
        var removed = current.Where(c => !requested.Contains(c)).Order(StringComparer.Ordinal).ToList();
        return new RolePermissionDiff(added, removed, requested.Count == 0);
    }
}
