using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Identity.Application.Email;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;
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
    /// Mặt còn lại: khai chuỗi kết nối và JWT tường minh thì Development dựng được app mà không cần
    /// <c>deploy/.env</c>. Đây chính là đường CI đi (ApiFactory) — mất tính chất này thì cổng hợp đồng API đỏ
    /// trên CI dù hợp đồng khớp.
    /// </summary>
    [Fact]
    public void Development_boots_without_deploy_env_when_connection_strings_and_jwt_are_explicit()
    {
        using var factory = new ApiFactory();

        using var client = factory.CreateClient();
        Assert.NotNull(client);
    }

    /// <summary>
    /// C4: thiếu khóa ký JWT, hoặc khóa ngắn hơn 32 byte (HS256 cần ≥ 256 bit), thì app từ chối khởi động —
    /// không có khóa mặc định. Chuỗi kết nối khai hợp lệ để chắc chắn app chết vì JWT chứ không vì DB.
    /// </summary>
    [Theory]
    [InlineData("Staging", "")]
    [InlineData("Production", "")]
    [InlineData("Staging", "0123456789abcdef")]   // 16 byte
    public void Missing_or_short_jwt_signing_key_must_fail_fast(string environment, string signingKey)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.UseSetting("ConnectionStrings:Postgres", ApiFactory.UnreachablePostgres);
                b.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
                b.UseSetting("Jwt:SigningKey", signingKey);
                b.UseSetting("Jwt:Issuer", TestJwt.Issuer);
                b.UseSetting("Jwt:Audience", TestJwt.Audience);
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Jwt__SigningKey", ex.Message, StringComparison.Ordinal);
        Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
        Assert.Contains(environment, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Đ-D9: ngoài Development thiếu một trong bốn key gửi mail thì từ chối khởi động, thông báo nêu đúng key và đúng
    /// biến môi trường. Mọi thứ khác khai hợp lệ để chắc chắn app chết vì đúng key đó.
    /// </summary>
    [Theory]
    [InlineData("Smtp:Host", "Smtp__Host")]
    [InlineData("Smtp:Port", "Smtp__Port")]
    [InlineData("Smtp:From", "Smtp__From")]
    [InlineData("Frontend:BaseUrl", "Frontend__BaseUrl")]
    public void Missing_email_config_must_fail_fast_outside_development(string key, string variable)
    {
        using var factory = StagingWithEmailConfig(b => b.UseSetting(key, string.Empty));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains(variable, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Staging", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Mặt còn lại của test trên: đủ bốn key thì Staging khởi động được — lưới không phải "luôn ném".</summary>
    [Fact]
    public void Staging_boots_when_email_config_is_complete()
    {
        using var factory = StagingWithEmailConfig(_ => { });

        using var client = factory.CreateClient();
        Assert.NotNull(client);
    }

    /// <summary>
    /// Đ-D9: Development không đặt Smtp:Host / Frontend:BaseUrl → mail đi localhost, link trỏ về frontend local, KHÔNG lấy
    /// giá trị staging trong deploy/.env. Gửi bằng SmtpEmailSender THẬT tới SMTP giả. Chỉ đặt Smtp:Port: Mailpit của
    /// compose dev có thể đang giữ cổng 1025 trên máy chạy test.
    /// </summary>
    [Fact]
    public async Task Development_khong_dat_cau_hinh_mail_link_tro_ve_localhost_3000()
    {
        await using var smtp = FakeSmtpServer.Start();
        using var api = new ApiFactory();
        using var factory = api.WithWebHostBuilder(b =>
            b.UseSetting("Smtp:Port", smtp.Port.ToString(CultureInfo.InvariantCulture)));
        var token = new string('a', 64);

        await factory.Services.GetRequiredService<IEmailSender>()
            .SendVerificationAsync("an.nguyen@example.com", token, CancellationToken.None);

        var raw = Assert.Single(smtp.Messages);
        Assert.Contains("an.nguyen@example.com", raw, StringComparison.Ordinal);
        Assert.Contains($"http://localhost:3000/verify-email?token={token}", FakeSmtpServer.DecodeBody(raw), StringComparison.Ordinal);
    }

    /// <summary>D4: ngoài Development thiếu origin CORS thì từ chối khởi động — thiếu là FE bị trình duyệt chặn ở mọi request.</summary>
    [Fact]
    public void Missing_cors_origins_must_fail_fast_outside_development()
    {
        using var factory = StagingWithEmailConfig(b => b.UseSetting("Cors:AllowedOrigins:0", string.Empty));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Cors__AllowedOrigins__0", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Staging", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// D4: trình duyệt gửi Origin dạng scheme://host[:port]. Cấu hình có "/" cuối hay path thì không bao giờ khớp mà không lỗi nào
    /// báo — nên từ chối khởi động, thông báo nêu đúng giá trị sai.
    /// </summary>
    [Theory]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/app")]
    [InlineData("app.example.com")]
    public void Invalid_cors_origin_must_fail_fast(string origin)
    {
        using var factory = StagingWithEmailConfig(b => b.UseSetting("Cors:AllowedOrigins:0", origin));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", ex.Message, StringComparison.Ordinal);
        Assert.Contains(origin, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// C1 (GĐ2, Đ-2.14): ngoài Development thiếu một trong bốn key R2 thì từ chối khởi động, thông báo nêu đúng key và đúng
    /// biến môi trường. Mọi thứ khác khai hợp lệ để chắc chắn app chết vì đúng key đó.
    /// </summary>
    [Theory]
    [InlineData("R2:Endpoint", "R2__Endpoint")]
    [InlineData("R2:Bucket", "R2__Bucket")]
    [InlineData("R2:AccessKey", "R2__AccessKey")]
    [InlineData("R2:SecretKey", "R2__SecretKey")]
    public void Missing_r2_config_must_fail_fast_outside_development(string key, string variable)
    {
        using var factory = StagingWithEmailConfig(b => b.UseSetting(key, string.Empty));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains(variable, ex.Message, StringComparison.Ordinal);
        Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Staging", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// C1: Endpoint kèm tên bucket (hay có path, hay http) là lỗi cấu hình có thật trong hướng dẫn khối C — SDK ghép bucket
    /// lần nữa và mọi lời gọi 404. Từ chối khởi động ở mọi môi trường khi giá trị có mà sai dạng.
    /// </summary>
    [Theory]
    [InlineData("https://test-account.r2.cloudflarestorage.com/socialapp-dev")]
    [InlineData("https://test-account.r2.cloudflarestorage.com/")]
    [InlineData("http://test-account.r2.cloudflarestorage.com")]
    public void Invalid_r2_endpoint_must_fail_fast(string endpoint)
    {
        using var factory = StagingWithEmailConfig(b => b.UseSetting("R2:Endpoint", endpoint));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("R2:Endpoint", ex.Message, StringComparison.Ordinal);
        Assert.Contains("R2__Endpoint", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Q-C1 (chốt 2026-09-19): Development KHÔNG có khóa R2 vẫn khởi động — đây là đường ApiFactory đi, tức là đường của
    /// smoke test và cổng hợp đồng API trên CI. Mất tính chất này thì cổng hợp đồng đỏ vì lý do không liên quan hợp đồng.
    /// Đổi lại, lời gọi IObjectStorage đầu tiên phải ném với thông điệp nêu đúng biến và đúng lệnh user-secrets — lỗi cấu
    /// hình lộ ra ở lúc dùng, với đúng lời, thay vì null reference vô danh.
    /// </summary>
    [Fact]
    public async Task Development_boots_without_r2_config_and_first_use_names_the_variables()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        Assert.True(live.IsSuccessStatusCode, $"/health/live trả {(int)live.StatusCode}");

        var storage = factory.Services.GetRequiredService<IObjectStorage>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.HeadAsync("posts/x/y.jpg"));

        Assert.Contains("R2__Endpoint", ex.Message, StringComparison.Ordinal);
        Assert.Contains("R2__SecretKey", ex.Message, StringComparison.Ordinal);
        Assert.Contains("user-secrets", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Development", ex.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> StagingWithEmailConfig(Action<IWebHostBuilder> tweak) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Staging");
            b.UseSetting("ConnectionStrings:Postgres", ApiFactory.UnreachablePostgres);
            b.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
            TestJwt.Configure(b);
            b.UseSetting("Smtp:Host", "mailpit");
            b.UseSetting("Smtp:Port", "1025");
            b.UseSetting("Smtp:From", "no-reply@example.com");
            b.UseSetting("Frontend:BaseUrl", "https://app.example.com");
            b.UseSetting("Cors:AllowedOrigins:0", "https://app.example.com");   // D4 — thiếu thì Staging từ chối khởi động
            // C1 (GĐ2) — thiếu thì Staging từ chối khởi động. Giá trị giả, không phải khóa thật.
            b.UseSetting("R2:Endpoint", "https://test-account.r2.cloudflarestorage.com");
            b.UseSetting("R2:Bucket", "socialapp-test");
            b.UseSetting("R2:AccessKey", "test-access-key");
            b.UseSetting("R2:SecretKey", "test-secret-key");
            tweak(b);
        });

    /// <summary>D0: hạn refresh token ≤ 0 thì cookie hết hạn ngay khi phát — từ chối khởi động thay vì mọi refresh 401.</summary>
    [Fact]
    public void Non_positive_refresh_token_days_must_fail_fast()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Staging");
                b.UseSetting("ConnectionStrings:Postgres", ApiFactory.UnreachablePostgres);
                b.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
                TestJwt.Configure(b);
                b.UseSetting("Jwt:RefreshTokenDays", "0");
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:RefreshTokenDays", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Development cũng không có khóa mặc định: không đặt Jwt:SigningKey và không có deploy/.env thì chết ngay.
    /// Content root ngoài repo để kết quả không phụ thuộc deploy/.env trên máy người chạy.
    /// </summary>
    [Fact]
    public void Development_without_jwt_signing_key_must_fail_fast_pointing_to_deploy_env()
    {
        var outsideRepo = Directory.CreateTempSubdirectory("startup-no-jwt-").FullName;
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.UseEnvironment(Environments.Development);
                    b.UseContentRoot(outsideRepo);
                    b.UseSetting("ConnectionStrings:Postgres", ApiFactory.UnreachablePostgres);
                    b.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
                    b.UseSetting("Jwt:SigningKey", string.Empty);
                    b.UseSetting("Jwt:Issuer", TestJwt.Issuer);
                    b.UseSetting("Jwt:Audience", TestJwt.Audience);
                });

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("Jwt__SigningKey", ex.Message, StringComparison.Ordinal);
            Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outsideRepo, recursive: true);
        }
    }

    /// <summary>
    /// A6 + Đ-4.3: hai contract chéo module phải resolve được từ container CỦA HOST.
    ///
    /// Vì sao cần một khẳng định riêng: từ A6, module Content chỉ chạy được khi host đã gọi
    /// <c>AddProfileModule</c> (IUserDirectory đăng ký ở đó, còn người dùng nó nằm ở Content). Từ GĐ4,
    /// <c>IFriendshipReader</c> đăng ký ở <c>AddSocialGraphModule</c> — thiếu thì request đọc bài nổ lúc resolve.
    /// Không có gì bắt lỗi lúc build.
    ///
    /// IUserDirectory / IFriendshipReader / IFeedSourceReader là scoped nên phải mở scope; resolve thẳng từ
    /// <c>factory.Services</c> sẽ ném vì lý do KHÁC hẳn và test sẽ nói dối.
    /// </summary>
    [Fact]
    public void Host_resolve_duoc_hai_contract_cheo_module_cua_A6_va_GD4()
    {
        using var factory = new ApiFactory();

        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserDirectory>());

        // Đ-4.3: đúng một đăng ký (hai lần thì thứ tự Add*Module quyết định BR-02 im lặng) và không phải AlwaysStrangers.
        var readers = scope.ServiceProvider.GetServices<IFriendshipReader>().ToList();
        Assert.Single(readers);
        Assert.IsNotType<AlwaysStrangers>(readers[0]);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFeedSourceReader>());
    }
}
