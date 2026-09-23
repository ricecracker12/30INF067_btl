namespace SocialApp.SharedKernel.Storage;

/// <summary>
/// Cấu hình Cloudflare R2 — MỘT nguồn cho presign (C2), HEAD lúc commit (C3/D5) và worker dọn rác (C4).
/// Chỉ <see cref="AccessKey"/> và <see cref="SecretKey"/> là bí mật: nằm ở biến môi trường / <c>deploy/.env</c>
/// (staging) hoặc <c>dotnet user-secrets</c> (dev), không bao giờ trong appsettings.
///
/// Fail-fast theo khuôn <c>JwtOptions</c> của GĐ1 nhưng CHỈ ngoài Development (Q-C1, chốt 2026-09-19): ApiFactory
/// (smoke + cổng hợp đồng API) chạy Development và cố ý không có khóa R2; fail-fast ở mọi môi trường là cổng hợp
/// đồng đỏ vì lý do không liên quan hợp đồng. Ở Development thiếu khóa thì app khởi động bình thường, lời gọi
/// <see cref="IObjectStorage"/> đầu tiên mới ném (<see cref="UnconfiguredObjectStorage"/>).
/// </summary>
public sealed class R2Options
{
    public const string Section = "R2";

    /// <summary>Hạn presigned PUT (SEQ-01 bước 3). HẰNG SỐ 10 phút — không cấu hình được.</summary>
    public const int PutUrlMinutes = 10;

    /// <summary>
    /// Hạn presigned GET (Đ-2.9). HẰNG SỐ 15 phút, KHÁC <see cref="PutUrlMinutes"/> một cách có chủ đích: PUT ngắn vì
    /// URL ghi được vào bucket; GET dài hơn vì một trang feed hiển thị trong nhiều phút. "Thống nhất cho gọn" là làm
    /// hỏng một trong hai.
    /// </summary>
    public const int GetUrlMinutes = 15;

    /// <summary><c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c> — gốc tài khoản, KHÔNG kèm tên bucket.</summary>
    public string Endpoint { get; init; } = "";
    public string Bucket { get; init; } = "";
    public string AccessKey { get; init; } = "";   // bí mật
    public string SecretKey { get; init; } = "";   // bí mật

    /// <summary>Tên key config còn trống, theo dạng <c>R2:Endpoint</c> — rỗng nghĩa là đủ.</summary>
    public IReadOnlyList<string> MissingKeys()
    {
        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(Endpoint)) missing.Add($"{Section}:{nameof(Endpoint)}");
        if (string.IsNullOrWhiteSpace(Bucket)) missing.Add($"{Section}:{nameof(Bucket)}");
        if (string.IsNullOrWhiteSpace(AccessKey)) missing.Add($"{Section}:{nameof(AccessKey)}");
        if (string.IsNullOrWhiteSpace(SecretKey)) missing.Add($"{Section}:{nameof(SecretKey)}");
        return missing;
    }

    public bool IsComplete => MissingKeys().Count == 0;

    /// <summary>
    /// Endpoint sai dạng thì SDK ghép thêm bucket lần nữa và mọi lời gọi 404 — triệu chứng không nói gì về nguyên nhân.
    /// Kiểm ở MỌI môi trường khi có giá trị (như <c>Cors:AllowedOrigins</c> của D4). Trả null nếu hợp lệ hoặc trống.
    /// </summary>
    public string? EndpointProblem()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
            return null;

        var ok = Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
              && uri.Scheme == "https"
              && uri.PathAndQuery == "/"
              && !Endpoint.EndsWith('/');
        return ok
            ? null
            : $"{Section}:{nameof(Endpoint)} phải có dạng https://<account-id>.r2.cloudflarestorage.com — https, không path, "
            + $"không tên bucket, không dấu / ở cuối (nhận được: '{Endpoint}')";
    }

    /// <summary>
    /// Thông điệp dùng chung cho fail-fast ở Program.cs và cho <see cref="UnconfiguredObjectStorage"/>: nêu đúng key,
    /// đúng biến môi trường, đúng chỗ sửa cho TỪNG môi trường — và nhắc luật Đ-2.14 (khóa dev không vào deploy/.env).
    /// </summary>
    public static string DescribeMissing(IReadOnlyList<string> missingKeys, string environmentName)
    {
        var variables = string.Join(", ", missingKeys.Select(k => k.Replace(':', '_').Replace("_", "__")));
        return $"Thiếu cấu hình R2 ({string.Join(", ", missingKeys)}) ở môi trường '{environmentName}'. "
             + $"Staging/Production: đặt {variables} trong deploy/.env trên server rồi deploy lại. "
             + "Development: dotnet user-secrets set \"R2:Endpoint\" \"https://<account-id>.r2.cloudflarestorage.com\" "
             + "-p src/backend/SocialApp.Api (và R2:Bucket, R2:AccessKey, R2:SecretKey — Mục 9.0 giai-doan-2.md). "
             + "KHÔNG đặt khóa dev vào deploy/.env: file đó mang giá trị staging (Đ-2.14).";
    }
}
