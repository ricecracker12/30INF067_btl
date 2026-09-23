namespace SocialApp.SharedKernel.Storage;

/// <summary>
/// Hiện thực đứng chỗ khi cấu hình R2 THIẾU ở Development (Q-C1): app khởi động bình thường (ApiFactory, smoke,
/// cổng hợp đồng không cần R2), và lời gọi đầu tiên mới ném với thông điệp nêu đúng bốn tên biến và lệnh
/// <c>user-secrets</c> phải chạy. Ngoài Development không bao giờ tới đây — Program.cs đã từ chối khởi động.
///
/// Không phải fake cho test: test dùng <c>FakeObjectStorage</c> (C5) trong project test. Lớp này chỉ để lỗi
/// cấu hình lộ ra ở đúng chỗ (lúc dùng) với đúng lời (chỗ sửa), thay vì null reference vô danh.
/// </summary>
internal sealed class UnconfiguredObjectStorage(R2Options options, string environmentName) : IObjectStorage
{
    private InvalidOperationException Missing() =>
        new(R2Options.DescribeMissing(options.MissingKeys(), environmentName));

    public string CreatePresignedPut(string key, string contentType, long contentLength) => throw Missing();

    public string CreatePresignedGet(string key) => throw Missing();

    public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) => throw Missing();

    public Task DeleteAsync(string key, CancellationToken ct = default) => throw Missing();

    public Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default) =>
        throw Missing();
}
