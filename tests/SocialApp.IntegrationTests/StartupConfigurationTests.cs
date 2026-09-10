using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Khóa hành vi khởi động khi cấu hình thiếu: ngoài Development, thiếu chuỗi kết nối thì app phải
/// CHẾT NGAY với thông báo nêu đúng key — không được rơi về giá trị mặc định rồi chạy tiếp.
///
/// Vì sao cần test cho một câu <c>throw</c>: hai kiểu hỏng câm ở đây đều từng tồn tại thật trong
/// repo này. Chuỗi rỗng trong appsettings.json khiến toán tử <c>??</c> không kích hoạt, và bản vá
/// đầu tiên (rơi về localhost) làm app khởi động bình thường rồi hỏng ngầm — trong container thì
/// localhost:5432 không có gì, /health/ready đỏ sau ~95 giây, mà Caddy vẫn proxy traffic vào.
/// Không có test thì lần refactor sau rất dễ vô tình quay lại một trong hai.
/// </summary>
public sealed class StartupConfigurationTests
{
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Missing_connection_string_must_fail_fast_outside_development(string environment)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                // Ép rỗng để test không phụ thuộc biến môi trường có sẵn trên máy chạy.
                b.UseSetting("ConnectionStrings:Postgres", string.Empty);
                b.UseSetting("ConnectionStrings:Redis", string.Empty);
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        // Thông báo phải chỉ thẳng vào KEY và CHỖ SỬA, không phải một exception chung chung từ tận
        // trong health check builder.
        Assert.Contains("ConnectionStrings:Postgres", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__Postgres", ex.Message, StringComparison.Ordinal);
        Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
        Assert.Contains(environment, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mặt còn lại của cùng một quyết định: Development vẫn phải chạy được bằng <c>dotnet run</c>
    /// mà không cần cấu hình gì. Mất tính chất này thì fail-fast trở thành phiền toái hằng ngày và
    /// sẽ có người gỡ nó ra.
    /// </summary>
    [Fact]
    public void Development_still_boots_without_any_connection_string_configured()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", string.Empty);
                b.UseSetting("ConnectionStrings:Redis", string.Empty);
            });

        // Không ném là đủ: app dựng được pipeline với giá trị fallback local.
        using var client = factory.CreateClient();
        Assert.NotNull(client);
    }
}
