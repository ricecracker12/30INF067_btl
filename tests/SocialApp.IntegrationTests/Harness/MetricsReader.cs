using System.Globalization;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Đọc một giá trị từ <c>/metrics</c> (định dạng text của Prometheus) — đọc qua HTTP thật chứ không qua registry, để test
/// khẳng định luôn cả tên chỉ số lẫn việc nó thật sự được xuất ra (GĐ7 C2).
///
/// Counter là biến tĩnh dùng chung cả process. Test so sánh TRƯỚC/SAU nên chỉ đúng khi không lớp nào khác chạm cùng
/// counter song song: mọi test đi qua đăng nhập, tạo bài, cấp ticket upload và worker dọn rác đều nằm trong
/// <see cref="PostgresCollection"/> — xUnit chạy tuần tự trong một collection. Test chạm các luồng đó mà đặt ngoài
/// collection này là số đếm lệch, test đỏ chập chờn.
/// </summary>
public static class MetricsReader
{
    /// <summary>
    /// Giá trị của <paramref name="name"/> (kèm <paramref name="labels"/> dạng <c>result="ran"</c> nếu có). Trả 0 khi dòng
    /// chưa xuất hiện: counter có nhãn chỉ hiện sau lần tăng đầu tiên của đúng bộ nhãn đó.
    /// </summary>
    public static async Task<double> ReadAsync(HttpClient client, string name, string? labels = null)
    {
        var text = await client.GetStringAsync("/metrics");
        var prefix = labels is null ? name + " " : $"{name}{{{labels}}} ";
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? 0 : double.Parse(line[prefix.Length..].Trim(), CultureInfo.InvariantCulture);
    }
}
