using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace SocialApp.SharedKernel.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> trên Cloudflare R2 qua <c>AWSSDK.S3</c> (Đ-2.14): R2 tương thích S3 một cách cố ý, không có
/// gói Cloudflare.R2 cho .NET, và ký sai một byte ở canonical request thì R2 trả <c>403 SignatureDoesNotMatch</c> — mà trên
/// trình duyệt triệu chứng trông y hệt CORS sai. Đó là nửa ngày dò nhầm hướng, không phải 150 dòng HMAC.
///
/// Ba thiết lập client, thiếu cái nào cũng hỏng theo cách khó đoán:
/// <list type="bullet">
/// <item><c>ForcePathStyle = true</c> — R2 yêu cầu path-style; thiếu thì SDK dựng <c>https://bucket.&lt;account&gt;...</c> và R2 từ chối tên miền.</item>
/// <item><c>AuthenticationRegion = "auto"</c> — R2 không có region; SDK vẫn cần một giá trị để ký SigV4.</item>
/// <item>Checksum <c>WHEN_REQUIRED</c> ở cả request lẫn response — SDK v4 mặc định <c>WHEN_SUPPORTED</c> gửi thêm header
/// checksum CRC mà R2 không hiểu, và lỗi trả về không nói gì về checksum.</item>
/// </list>
///
/// Presign là HMAC cục bộ, không gọi mạng — ký 20 URL cho một trang feed không tốn lời gọi nào. Ba thao tác còn lại
/// (HEAD, Delete, List) mới đi mạng. Không bao giờ log URL đã ký: nó mang chữ ký, log nó là để lại một URL ghi được vào
/// bucket trong hệ thống log (B.9 điểm 4).
/// </summary>
public sealed class R2ObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public R2ObjectStorage(R2Options options)
    {
        if (!options.IsComplete)
            throw new ArgumentException("R2Options chưa đủ — Program.cs phải fail-fast trước khi tới đây.", nameof(options));

        var config = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        _client = new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        _bucket = options.Bucket;
    }

    public string CreatePresignedPut(string key, string contentType, long contentLength)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(R2Options.PutUrlMinutes),
            ContentType = contentType,
        };
        // Content-Length phải NẰM TRONG signed headers, không chỉ là gợi ý cho client: presigned PUT không tự giới hạn
        // dung lượng — ký cho 2MB rồi client PUT 400MB vẫn vào bucket nếu Content-Length không được ký (Đ-2.8 lớp 1).
        // Đây cũng là lý do D4 phải trả `requiredHeaders` cho FE: PUT thiếu một header đã ký là 403 từ R2.
        request.Headers.ContentLength = contentLength;

        return _client.GetPreSignedURL(request);
    }

    public string CreatePresignedGet(string key) =>
        _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(R2Options.GetUrlMinutes),
        });

    public async Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct);
            return new ObjectHead(response.ContentLength, response.Headers.ContentType ?? "", ToOffset(response.LastModified));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Đúng MỘT trường hợp trả null: object không có. Mạng hỏng, khóa sai, bucket sai → ném ra ngoài — đó là 500,
            // không phải "ảnh của bạn không hợp lệ" (MediaHeadPolicy chỉ nhận null cho nghĩa "chưa tải lên xong").
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _client.DeleteObjectAsync(_bucket, key, ct);   // S3/R2: xóa object không tồn tại vẫn 204 — idempotent như hợp đồng interface

    public async Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default)
    {
        var response = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = prefix,
            ContinuationToken = continuationToken,
            MaxKeys = maxKeys,
        }, ct);

        var items = (response.S3Objects ?? [])
            .Select(o => new ObjectItem(o.Key, SizeOf(o.Size), ToOffset(o.LastModified)))
            .ToList();

        return new ObjectPage(items, response.IsTruncated == true ? response.NextContinuationToken : null);
    }

    public void Dispose() => _client.Dispose();

    // SDK v4 đổi nhiều thuộc tính sang nullable (Size, LastModified, IsTruncated); nhận kiểu nullable để biên dịch được
    // với cả hai hình dạng và không rải `?? ` khắp nơi.
    private static long SizeOf(long? size) => size ?? 0;

    private static DateTimeOffset ToOffset(DateTime? utc) =>
        utc is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero) : DateTimeOffset.UnixEpoch;
}
