using System.Collections.Concurrent;

namespace SocialApp.SharedKernel.Redis;

/// <summary>
/// Giới hạn tần suất cảnh báo fail-open khi Redis không tới được: mỗi LOẠI cảnh báo ghi tối đa một dòng mỗi
/// <see cref="Window"/>, dòng kế tiếp mang số lần đã bị nuốt.
///
/// Vì sao cần (báo cáo k6 sơ bộ GĐ4, lượt 3): Redis dừng thì mọi request đi qua 2–3 chỗ fail-open, mỗi chỗ một dòng Warning —
/// khoảng 440.000 dòng trong 4 phút ở 500 request/s, đúng lúc api đã chạm trần CPU. Log ngập không cho thêm thông tin nào
/// (dòng thứ hai giống hệt dòng đầu), chỉ tốn CPU và đẩy log thật ra khỏi tầm nhìn.
///
/// SINGLETON theo từng host (đăng ký trong <see cref="RedisExtensions.AddSharedKernelRedis"/>), KHÔNG <c>static</c>: trạng thái
/// tĩnh sống chung cả tiến trình test, và ca test dựng host mới để khẳng định "có log cảnh báo" sẽ xanh/đỏ theo thứ tự chạy.
/// Dòng ĐẦU TIÊN của mỗi loại luôn được ghi ngay — người trực nhìn thấy sự cố ở giây đầu, không phải sau 30s.
/// </summary>
public sealed class FailOpenLogThrottle(TimeProvider clock)
{
    /// <summary>Một dòng mỗi loại mỗi 30s: đủ thấy sự cố còn kéo dài, không đủ để ngập log.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    /// <summary>
    /// <c>true</c> → người gọi ghi log NGAY, kèm <paramref name="suppressed"/> = số lần cùng loại đã bị nuốt kể từ dòng trước.
    /// <c>false</c> → không ghi; lần này được đếm vào dòng kế tiếp.
    /// </summary>
    /// <param name="kind">Tên loại cảnh báo, cố định theo chỗ gọi (không chứa dữ liệu người dùng).</param>
    public bool ShouldLog(string kind, out long suppressed)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var slot = _slots.GetOrAdd(kind, static _ => new Slot());
        var now = clock.GetUtcNow();

        // Khóa theo TỪNG loại: một phép so sánh và hai phép gán — rẻ hơn nhiều so với dòng log mà nó thay.
        lock (slot)
        {
            if (now < slot.NextAllowed)
            {
                slot.Suppressed++;
                suppressed = 0;
                return false;
            }

            suppressed = slot.Suppressed;
            slot.Suppressed = 0;
            slot.NextAllowed = now + Window;
            return true;
        }
    }

    private sealed class Slot
    {
        public DateTimeOffset NextAllowed = DateTimeOffset.MinValue;
        public long Suppressed;
    }
}
