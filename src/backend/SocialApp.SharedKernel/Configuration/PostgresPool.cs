using System.Data.Common;

namespace SocialApp.SharedKernel.Configuration;

/// <summary>
/// Trần pool kết nối Postgres mặc định của app (PERF-03, báo cáo k6 sơ bộ GĐ4 Mục 5).
///
/// Npgsql mặc định <c>Maximum Pool Size = 100</c> — đúng bằng <c>max_connections = 100</c> mặc định của Postgres. Lúc Redis
/// dừng (lượt 3 của k6), mọi request đọc thêm DB và pool của app lớn tới đủ 100: app không lỗi, nhưng <c>migrate</c>, backup,
/// <c>psql</c> của người trực và instance api thứ hai đều bị từ chối <c>too many clients already</c>. Đo lại với 80: đỉnh 81,
/// p95 không kém, 0 % lỗi — còn 20 chỗ cho mọi thứ khác.
///
/// Đặt Ở CODE chứ không chỉ trong <c>deploy/.env</c>: máy nào cũng được bảo vệ mà không phụ thuộc ai nhớ sửa file cấu hình.
/// Chuỗi kết nối đã ghi con số tường minh thì GIỮ NGUYÊN — môi trường có <c>max_connections</c> khác chỉnh ở đó.
/// </summary>
public static class PostgresPool
{
    /// <summary>100 (<c>max_connections</c> mặc định của Postgres 16) trừ 20 chỗ cho migrate/backup/psql/instance thứ hai.</summary>
    public const int DefaultMaxPoolSize = 80;

    /// <summary>
    /// Hai cách viết Npgsql 8 chấp nhận cho cùng một khóa (tên hiển thị và tên thuộc tính). <see cref="DbConnectionStringBuilder"/>
    /// so khóa không phân biệt hoa thường, nhưng KHÔNG biết từ đồng nghĩa — thiếu một cách viết ở đây là ghi đè con số người
    /// vận hành đã đặt. "Max Pool Size" (cách viết của SqlClient) Npgsql từ chối ngay lúc mở kết nối — không cần nhận ở đây.
    /// </summary>
    private static readonly string[] MaxPoolSizeKeywords = ["Maximum Pool Size", "MaxPoolSize"];

    /// <summary>
    /// Trả chuỗi kết nối có <c>Maximum Pool Size</c>: giữ nguyên nếu đã có (mọi cách viết), thêm
    /// <see cref="DefaultMaxPoolSize"/> nếu chưa. Không đụng khóa nào khác.
    /// </summary>
    public static string WithDefaultMaxPoolSize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (MaxPoolSizeKeywords.Any(builder.ContainsKey))
            return connectionString;

        builder["Maximum Pool Size"] = DefaultMaxPoolSize;
        return builder.ConnectionString;
    }
}
