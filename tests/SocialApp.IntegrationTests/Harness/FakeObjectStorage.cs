using System.Collections.Concurrent;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// C5 (GĐ2, Mục 10.2 mức 2): <see cref="IObjectStorage"/> in-memory để test của D3/D4/D5 và B3 chạy trên CI KHÔNG có khóa
/// R2 — thay cho việc viết test gọi R2 thật rồi <c>Skip</c> khi thiếu khóa (test bị skip là test không tồn tại, mà lại
/// tạo cảm giác đã có lưới).
///
/// Sống trong project TEST, cạnh <see cref="FakeSmtpServer"/>. Không có <c>#if DEBUG</c> nào trong code sản phẩm; test đăng
/// ký nó qua <c>ConfigureTestServices</c> (xem <c>AuthZApiFactory</c>). KHÔNG dùng để nghiệm thu C2 — C2 nghiệm thu bằng R2
/// thật trên trình duyệt, vì <c>curl</c> lẫn fake đều không bao giờ thấy CORS.
///
/// URL trả về CỐ Ý không giống URL R2 thật và không mang chữ ký nào (<c>fake.invalid</c> — RFC 2606 dành riêng tên miền
/// này cho đúng việc này). Test nào khẳng định điều gì về chữ ký thì phải là unit test của C2, không phải test dùng lớp này.
/// </summary>
public sealed class FakeObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, ObjectHead> _objects = new(StringComparer.Ordinal);

    /// <summary>Số lời gọi HEAD — để test canh Đ-2.8 lớp 2 khẳng định D5 thật sự HEAD từng key, không tin khai báo.</summary>
    public int HeadCalls => _headCalls;
    private int _headCalls;

    /// <summary>Key đã bị xóa, theo thứ tự — cho test của worker dọn rác (C4).</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>
    /// Dựng "object đã tồn tại trong bucket với size/type này" — cả lý do lớp này tồn tại. Mốc thời gian mặc định là bây
    /// giờ; test của worker (24 giờ, 7 ngày) truyền <paramref name="lastModified"/> cũ hơn.
    /// </summary>
    public void Put(string key, long contentLength, string contentType, DateTimeOffset? lastModified = null) =>
        _objects[key] = new ObjectHead(contentLength, contentType, lastModified ?? DateTimeOffset.UtcNow);

    public bool Exists(string key) => _objects.ContainsKey(key);

    public string CreatePresignedPut(string key, string contentType, long contentLength) =>
        $"https://fake.invalid/put/{key}";

    public string CreatePresignedGet(string key) => $"https://fake.invalid/get/{key}";

    public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _headCalls);
        return Task.FromResult(_objects.TryGetValue(key, out var head) ? head : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        // Idempotent như hợp đồng của interface: xóa thứ không có không phải lỗi.
        if (_objects.TryRemove(key, out _))
            Deleted.Add(key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Phân trang thật (không phải trả hết một lần): worker C4 dựa vào continuation token để không giữ khóa suốt cả tiếng,
    /// và test của nó phải thấy được nhiều trang. Token là chỉ số bắt đầu của trang kế — đủ cho fake, không cần mã hóa.
    /// </summary>
    public Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default)
    {
        if (maxKeys <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxKeys), "maxKeys phải > 0");

        var all = _objects
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ObjectItem(kv.Key, kv.Value.ContentLength, kv.Value.LastModified))
            .ToList();

        var start = continuationToken is null ? 0 : int.Parse(continuationToken);
        var page = all.Skip(start).Take(maxKeys).ToList();
        var next = start + maxKeys < all.Count ? (start + maxKeys).ToString() : null;
        return Task.FromResult(new ObjectPage(page, next));
    }
}
