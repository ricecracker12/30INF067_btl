using Npgsql;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// B1 của GĐ6 (đi cùng D2 — L-D7 của hướng dẫn khối D): dựng tài khoản và vai trò bằng SQL cho test của màn quản trị. D3–D5 dùng
/// lại. SQL chứ không qua API: đăng ký thật tốn một lần BCrypt + một mail mỗi tài khoản, và nhóm <c>auth</c> giới hạn 10 req/phút
/// theo IP — 45 tài khoản của <c>ADM-07</c> là 5 phút chờ. Vai trò tự tạo đi SQL vì API vai trò là D5.
///
/// <c>password_hash</c> mặc định là chuỗi giả: tài khoản không đăng nhập được, test ký token bằng <c>TestJwt</c>. Test cần đăng nhập
/// THẬT (D3 <c>ADM-01</c>: refresh family, login bị khóa) truyền hash BCrypt thật — băm bằng <c>IPasswordHasher</c> của app.
///
/// Mỗi lời gọi mở một kết nối tới CÙNG chuỗi kết nối của app → chung pool. Lớp test dùng helper này nên <c>ClearPool</c> ở
/// <c>DisposeAsync</c> (cạm bẫy 7 Mục 3 của hướng dẫn khối A+C).
/// </summary>
public static class IdentitySql
{
    /// <summary>
    /// Một tài khoản đã xác minh email. <paramref name="createdAt"/> ghi tường minh để test keyset dựng được nhiều tài khoản CÙNG
    /// một thời điểm (cạm bẫy 4 của D2); để trống thì <c>now()</c> của DB.
    /// </summary>
    public static async Task<Guid> TaoTaiKhoanAsync(
        string connectionString,
        string email,
        string roleCode = "USER",
        string status = "active",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lockedUntil = null,
        bool emailVerified = true,
        string passwordHash = "khong-phai-hash")
    {
        var userId = Guid.NewGuid();
        await ExecAsync(connectionString, """
            INSERT INTO identity.users (user_id, email, password_hash, role_id, status, email_verified_at, locked_until, created_at)
            VALUES ($1, $2, $8, (SELECT role_id FROM identity.roles WHERE code = $3), $4,
                    CASE WHEN $5 THEN now() END, $6, COALESCE($7, now()))
            """,
            userId, email, roleCode, status, emailVerified,
            (object?)lockedUntil ?? DBNull.Value, (object?)createdAt ?? DBNull.Value, passwordHash);
        return userId;
    }

    /// <summary>
    /// Một Admin hoạt động. Seed KHÔNG tạo tài khoản Admin nào (chỉ vai trò) — database test không có Admin thì bất biến Đ-6.7 chặn
    /// MỌI lần khóa/hạ quyền bằng 409, nên lớp test của D3/D4 dựng Admin trước tiên. Test bất biến dựng đúng số Admin nó cần.
    /// </summary>
    public static Task<Guid> TaoAdminThuHaiAsync(string connectionString) =>
        TaoTaiKhoanAsync(connectionString, $"admin-{Guid.NewGuid():N}@test.local", "ADMIN");

    /// <summary>Đổi vai trò của một tài khoản có sẵn. Vai trò phải tồn tại: <c>role_id</c> NOT NULL làm câu lệnh ném nếu không.</summary>
    public static Task DatVaiTroAsync(string connectionString, Guid userId, string roleCode) =>
        ExecAsync(connectionString,
            "UPDATE identity.users SET role_id = (SELECT role_id FROM identity.roles WHERE code = $1) WHERE user_id = $2",
            roleCode, userId);

    /// <summary>
    /// Vai trò tự tạo có đúng tập <paramref name="quyen"/> — id lấy từ sequence của A3, như API D5 sẽ làm. Mã quyền không tồn tại
    /// thì bị bỏ qua (join), nên test nên khẳng định trên hành vi, không trên số dòng.
    /// </summary>
    public static Task TaoVaiTroAsync(string connectionString, string code, params string[] quyen) =>
        ExecAsync(connectionString, """
            WITH r AS (
                INSERT INTO identity.roles (role_id, code, display_name)
                VALUES (nextval('identity.roles_role_id_seq'), $1, $1) RETURNING role_id)
            INSERT INTO identity.role_permissions (role_id, permission_id)
            SELECT r.role_id, p.permission_id FROM r, identity.permissions p WHERE p.code = ANY($2)
            """,
            code, quyen);

    /// <summary>Mã vai trò ngẫu nhiên đúng dạng <c>RoleCode</c> (<c>^[A-Z][A-Z0-9_]{2,29}$</c>).</summary>
    public static string MaVaiTroMoi(string tienTo) => $"{tienTo}_{Guid.NewGuid():N}"[..Math.Min(30, tienTo.Length + 13)].ToUpperInvariant();

    private static async Task ExecAsync(string connectionString, string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        await cmd.ExecuteNonQueryAsync();
    }
}
