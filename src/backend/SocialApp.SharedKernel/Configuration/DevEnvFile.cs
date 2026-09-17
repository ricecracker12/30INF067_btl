namespace SocialApp.SharedKernel.Configuration;

/// <summary>
/// Đọc cấu hình dev từ <c>deploy/.env</c> — nguồn DUY NHẤT của bí mật ở máy dev (mật khẩu Postgres, khóa ký
/// JWT): compose dev đọc cùng file để dựng container, nên code chạy trên máy host (<c>dotnet run</c>,
/// <c>dotnet ef</c>) cũng đọc từ đây thay vì giữ một bản sao ghi cứng trong repo.
///
/// Chỉ dùng cho đường chạy ở máy dev: fallback Development của Program.cs và design-time factory của EF.
/// Container, staging và test luôn nhận chuỗi kết nối tường minh (biến môi trường, <c>ApiFactory</c>,
/// Testcontainers), không đi qua lớp này.
///
/// BẮT BUỘC có file: <see cref="LocalPostgresConnectionString"/> ném khi thiếu mật khẩu. Ai làm việc với DB
/// dev phải có <c>deploy/.env</c> — không có mật khẩu mặc định, không có đường tắt chạy tiếp với cấu hình
/// thiếu.
/// </summary>
public static class DevEnvFile
{
    /// <summary>
    /// Chuỗi kết nối tới Postgres của compose dev (cổng 5432 publish ra localhost), mật khẩu lấy từ
    /// <c>POSTGRES_PASSWORD</c>. Ném <see cref="InvalidOperationException"/> nếu không có <c>deploy/.env</c>
    /// hoặc biến để trống, thông báo nêu đúng chỗ sửa.
    /// </summary>
    public static string LocalPostgresConnectionString(string startDirectory)
    {
        var password = FindValue("POSTGRES_PASSWORD", startDirectory)
            ?? throw new InvalidOperationException(
                $"Thiếu POSTGRES_PASSWORD trong deploy/.env (tìm ngược từ '{startDirectory}'). "
              + "Chép deploy/.env.example thành deploy/.env rồi điền POSTGRES_PASSWORD — cùng giá trị compose dev "
              + "dùng. Muốn trỏ vào DB khác thì đặt ConnectionStrings__Postgres. Không có mật khẩu mặc định: "
              + "từ chối chạy thay vì tiếp tục với cấu hình thiếu.");

        return $"Host=localhost;Port=5432;Database=socialapp;Username=socialapp;Password={password}";
    }

    /// <summary>Tên biến khóa ký JWT trong <c>deploy/.env</c> — trùng tên biến môi trường container đọc.</summary>
    public const string JwtSigningKeyVariable = "Jwt__SigningKey";

    /// <summary>
    /// Khóa ký JWT cho đường chạy dev trên máy host, đọc <c>Jwt__SigningKey</c> trong <c>deploy/.env</c>. Trả
    /// <c>null</c> khi thiếu — KHÔNG ném ở đây: Program.cs gộp "thiếu" và "quá ngắn" vào cùng một kiểm tra
    /// JwtOptions, một thông báo nêu đúng chỗ sửa. Không có khóa mặc định.
    /// </summary>
    public static string? LocalJwtSigningKey(string startDirectory) => FindValue(JwtSigningKeyVariable, startDirectory);

    /// <summary>
    /// Đi ngược từ <paramref name="startDirectory"/> lên tới thư mục gốc repo — nhận ra nhờ file đã commit
    /// <c>deploy/.env.example</c>, nên không bao giờ lọt ra ngoài repo — rồi đọc <paramref name="key"/> trong
    /// <c>deploy/.env</c> cạnh nó. Không có file / không có biến / giá trị rỗng thì trả <c>null</c>: đây là hàm
    /// tra cứu thuần, việc bắt buộc thuộc về người gọi.
    /// </summary>
    public static string? FindValue(string key, string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var deploy = Path.Combine(dir.FullName, "deploy");
            if (!File.Exists(Path.Combine(deploy, ".env.example")))
                continue;

            var envFile = Path.Combine(deploy, ".env");
            return File.Exists(envFile) ? ReadValue(envFile, key) : null;
        }

        return null;
    }

    private static string? ReadValue(string path, string key)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0 || line[..eq].Trim() != key)
                continue;

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
                value = value[1..^1];

            return value.Length == 0 ? null : value;
        }

        return null;
    }
}
