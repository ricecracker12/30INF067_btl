using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Khóa hành vi khởi động khi cấu hình thiếu: ở MỌI môi trường, thiếu cấu hình thì app phải CHẾT NGAY với
/// thông báo nêu đúng chỗ sửa — không được rơi về giá trị mặc định rồi chạy tiếp.
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
    /// Development cũng không có đường tắt: không đặt chuỗi kết nối thì Program.cs dựng nó từ
    /// <c>deploy/.env</c>, và thiếu file đó thì phải chết ngay với thông báo chỉ đúng chỗ sửa — không có mật
    /// khẩu mặc định (AGENTS.md Mục 13).
    ///
    /// Content root trỏ vào một thư mục tạm không nằm dưới repo nào, để kết quả không phụ thuộc
    /// <c>deploy/.env</c> trên máy người chạy (máy dev có file, CI thì không).
    /// </summary>
    [Fact]
    public void Development_without_deploy_env_must_fail_fast_pointing_to_deploy_env()
    {
        var outsideRepo = Directory.CreateTempSubdirectory("startup-no-env-").FullName;
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.UseEnvironment(Environments.Development);
                    b.UseContentRoot(outsideRepo);
                    b.UseSetting("ConnectionStrings:Postgres", string.Empty);
                    b.UseSetting("ConnectionStrings:Redis", string.Empty);
                });

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
            Assert.Contains("POSTGRES_PASSWORD", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outsideRepo, recursive: true);
        }
    }

    /// <summary>
    /// Mặt còn lại: khai chuỗi kết nối tường minh thì Development dựng được app mà không cần
    /// <c>deploy/.env</c>. Đây chính là đường CI đi (ApiFactory) — mất tính chất này thì cổng hợp đồng API đỏ
    /// trên CI dù hợp đồng khớp.
    /// </summary>
    [Fact]
    public void Development_boots_without_deploy_env_when_connection_strings_are_explicit()
    {
        using var factory = new ApiFactory();

        using var client = factory.CreateClient();
        Assert.NotNull(client);
    }
}
