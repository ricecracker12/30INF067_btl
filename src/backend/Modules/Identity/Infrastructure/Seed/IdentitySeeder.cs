using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Seed;

/// <summary>
/// Nạp dữ liệu nền phân quyền (Mục 5): 3 vai trò, 18 quyền (17 của GĐ1 + <c>role.manage</c> của GĐ6 — tự có vì đọc
/// <see cref="PermissionCodes.All"/>), 24 dòng gán quyền. Chạy ở hook
/// <c>--migrate</c> ngay sau <c>MigrateAsync</c>, mỗi lần deploy — nên phải chạy lại bao nhiêu lần cũng
/// được, và không được đè cấu hình mà Admin đã sửa lúc runtime (Mục 5.4).
///
/// Ba luật:
/// 1. Idempotent ở tầng DB (<c>ON CONFLICT</c>), không đọc-rồi-ghi ở tầng app — hai instance chạy cùng lúc
///    thì "đọc thấy chưa có → ghi" sẽ chèn trùng.
/// 2. <c>roles</c> / <c>permissions</c>: <c>ON CONFLICT DO NOTHING</c>, KHÔNG <c>DO UPDATE</c>. Muốn đổi
///    <c>display_name</c> / <c>description</c> của dữ liệu nền thì làm bằng migration. KHÔNG nêu cột
///    conflict: kịch bản ADMIN→ROOT (Mục 3.1) làm <c>code</c> hết xung đột nhưng <c>role_id = 3</c> vẫn xung
///    đột — nêu <c>(code)</c> thì seeder chết bằng một lỗi unique violation khó đọc trước khi kiểm tra vai
///    trò hệ thống (A5) kịp báo đúng nguyên nhân.
/// 3. <c>role_permissions</c>: bootstrap MỘT LẦN cho mỗi vai trò (<c>WHERE NOT EXISTS</c>). Bảng này không có
///    payload — sự tồn tại của dòng chính là quyền — nên Admin gỡ một quyền thì dòng biến mất, và
///    <c>DO NOTHING</c> một mình sẽ chèn lại đúng quyền vừa gỡ (SEED-02).
///
/// Ranh giới của luật 3: vai trò bị gỡ HẾT quyền trông giống vai trò chưa từng seed, nên lần deploy sau
/// được cấp lại đủ bộ mặc định. GĐ1 chấp nhận vì chưa có endpoint sửa quyền; GĐ6 nếu cần một vai trò
/// không có quyền nào lâu dài thì phải thêm dấu vết seed (ví dụ bảng <c>seed_history</c>).
///
/// SQL dựng bằng <c>ExecuteSqlRawAsync</c> vì tên schema không tham số hóa được. Mọi giá trị nội suy là
/// hằng số trong repo (<see cref="RoleCodes"/>, <see cref="PermissionCodes"/>), không có input người dùng.
/// Luôn ghi rõ schema: <c>HasDefaultSchema</c> chỉ tác động LINQ, SQL thô đi thẳng xuống với
/// <c>search_path</c> mặc định là <c>public</c>.
/// </summary>
public static class IdentitySeeder
{
    private const string S = IdentityDbContext.Schema;

    // Mục 5.1 — role_id gán tay.
    private static readonly (short Id, string Code, string DisplayName)[] Roles =
    [
        (1, RoleCodes.User, "Người dùng"),
        (2, RoleCodes.Moderator, "Kiểm duyệt viên"),
        (3, RoleCodes.Admin, "Quản trị viên"),
    ];

    // Mục 5.3.
    private static readonly string[] UserGrants =
    [
        PermissionCodes.PostReadPublic,
        PermissionCodes.PostReadFriends,
        PermissionCodes.PostCreate,
        PermissionCodes.PostUpdate,
        PermissionCodes.PostDelete,
        PermissionCodes.CommentCreate,
        PermissionCodes.ReactionSet,
        PermissionCodes.FriendRequest,
        PermissionCodes.FriendRespond,
        PermissionCodes.MessageSend,
        PermissionCodes.ReportCreate,
    ];

    private static readonly string[] ModeratorGrants =
        [.. UserGrants, PermissionCodes.PostHide, PermissionCodes.ReportResolve];

    // ADMIN CỐ Ý KHÔNG CÓ DÒNG role_permissions NÀO. Đây là THIẾT KẾ (quyết định 3.2): mọi quyền của ADMIN
    // đến từ short-circuit ở tầng 2, không phải từ bảng này. Đừng "sửa lỗi" bằng cách seed thêm quyền cho
    // ADMIN — làm vậy thì ma trận có hai nguồn sự thật và gỡ quyền của ADMIN trong bảng không còn nghĩa gì.

    /// <summary>
    /// Seed dữ liệu nền rồi kiểm tra vai trò hệ thống. Ném <see cref="InvalidOperationException"/> nếu thiếu
    /// vai trò nào trong <see cref="RoleCodes.All"/> — người gọi KHÔNG được nuốt ngoại lệ này: bước deploy
    /// phải thất bại, không được chạy tiếp trên dữ liệu nền hỏng.
    /// </summary>
    public static async Task SeedAsync(IdentityDbContext db, CancellationToken ct = default)
    {
        // Đúng thứ tự khóa ngoại: roles → permissions → role_permissions.
        await db.Database.ExecuteSqlRawAsync(RolesSql(), ct);
        await db.Database.ExecuteSqlRawAsync(PermissionsSql(), ct);
        await db.Database.ExecuteSqlRawAsync(GrantsSql(RoleId(RoleCodes.User), UserGrants), ct);
        await db.Database.ExecuteSqlRawAsync(GrantsSql(RoleId(RoleCodes.Moderator), ModeratorGrants), ct);

        await EnsureSystemRolesAsync(db, ct);
    }

    /// <summary>
    /// Kiểm tra vai trò hệ thống (Mục 5.5) — biện pháp #2 thay cho cột <c>is_system</c> (Mục 3.4). Ai đó
    /// <c>UPDATE roles SET code = 'ROOT'</c> bằng tay thì short-circuit <c>role == "ADMIN"</c> ở tầng 2 hết
    /// khớp, mà ADMIN lại cố ý không có dòng <c>role_permissions</c> nào để rơi về: mất sạch quyền quản
    /// trị, im lặng, không đường phục hồi. Kiểm tra này biến chuyện đó thành một lần deploy thất bại có
    /// thông báo nêu đúng tên vai trò thiếu.
    ///
    /// Ranh giới:
    /// - BẮT ĐƯỢC: mất bất kỳ mã nào trong <see cref="RoleCodes.All"/> (ADMIN→ROOT, đổi hoa thường...), ở lần
    ///   deploy kế tiếp.
    /// - KHÔNG BẮT ĐƯỢC: thời điểm ai đó gõ <c>UPDATE</c> trong psql. App đang chạy vẫn hỏng cho tới lần
    ///   deploy/restart. Chặn đúng lúc ghi cần trigger DB — chỉ đáng làm khi GĐ6 có endpoint sửa vai trò thật.
    ///
    /// Đặt trong seeder chứ không phải một IHostedService riêng: seeder đã chạy mỗi lần deploy nên kiểm tra
    /// không thể bị quên đăng ký. Giá phải trả là một câu SELECT trên 3 dòng. Nó chạy tới được đây là nhờ
    /// <c>roles</c> dùng <c>ON CONFLICT DO NOTHING</c> không nêu cột (luật 2 ở đầu lớp).
    /// </summary>
    private static async Task EnsureSystemRolesAsync(IdentityDbContext db, CancellationToken ct)
    {
        var actual = await db.Roles.Select(r => r.Code).ToListAsync(ct);
        var missing = RoleCodes.All.Except(actual).ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Thiếu vai trò hệ thống: {string.Join(", ", missing)}. " +
                "Có thể ai đó đã đổi roles.code bằng tay. App từ chối khởi động.");
    }

    private static string RolesSql() =>
        $"""
        INSERT INTO {S}.roles (role_id, code, display_name) VALUES
        {string.Join(",\n", Roles.Select(r => $"  ({r.Id}, {Literal(r.Code)}, {Literal(r.DisplayName)})"))}
        ON CONFLICT DO NOTHING;
        """;

    // permission_id = vị trí trong PermissionCodes.All + 1, khớp Mục 5.2. description lấy từ PermissionCodes.Descriptions (GĐ6
    // L-A2): DB MỚI nhận mô tả ngay lúc seed — migration SystemRoleGuardAndPermissionDescriptions chạy TRƯỚC seeder nên không
    // điền được cho DB mới. DO NOTHING vẫn giữ luật 2: dòng đã có (staging, mô tả Admin đã sửa) không bao giờ bị ghi đè.
    private static string PermissionsSql() =>
        $"""
        INSERT INTO {S}.permissions (permission_id, code, description) VALUES
        {string.Join(",\n", PermissionCodes.All.Select((code, i) =>
            $"  ({i + 1}, {Literal(code)}, {Literal(PermissionCodes.Descriptions[code])})"))}
        ON CONFLICT DO NOTHING;
        """;

    private static string GrantsSql(short roleId, string[] codes) =>
        $"""
        INSERT INTO {S}.role_permissions (role_id, permission_id)
        SELECT {roleId}, p FROM unnest(ARRAY[{string.Join(", ", codes.Select(PermissionId))}]::smallint[]) AS p
        WHERE NOT EXISTS (SELECT 1 FROM {S}.role_permissions WHERE role_id = {roleId})
        ON CONFLICT DO NOTHING;
        """;

    private static short RoleId(string code) => Roles.Single(r => r.Code == code).Id;

    private static short PermissionId(string code)
    {
        var index = Array.IndexOf(PermissionCodes.All, code);
        if (index < 0)
            throw new InvalidOperationException($"Mã quyền '{code}' không có trong PermissionCodes.All.");
        return (short)(index + 1);
    }

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";
}
