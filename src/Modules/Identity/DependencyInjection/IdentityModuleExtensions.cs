using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Application.Email;
using SocialApp.Modules.Identity.Application.Login;
using SocialApp.Modules.Identity.Application.Me;
using SocialApp.Modules.Identity.Application.Session;
using SocialApp.Modules.Identity.Application.Registration;
using SocialApp.Modules.Identity.Application.Security;
using SocialApp.Modules.Identity.Infrastructure;
using SocialApp.Modules.Identity.Infrastructure.Authorization;
using SocialApp.Modules.Identity.Infrastructure.Email;
using SocialApp.Modules.Identity.Infrastructure.Persistence;
using SocialApp.Modules.Identity.Infrastructure.Security;
using SocialApp.Modules.Identity.Infrastructure.Seed;
using SocialApp.SharedKernel.Authorization;

namespace SocialApp.Modules.Identity.DependencyInjection;

/// <summary>
/// Điểm ráp DI duy nhất của module Identity. Api chỉ gọi <see cref="AddIdentityModule"/>; mọi chi
/// tiết EF nằm lại trong module (ADR-001).
/// </summary>
public static class IdentityModuleExtensions
{
    /// <summary>
    /// Schema Postgres của module, công bố lại ở tầng DI để host không phải <c>using</c> vào
    /// namespace Infrastructure chỉ để lấy một hằng số (ADR-001: host chỉ biết bề mặt DI của module).
    /// </summary>
    public const string Schema = IdentityDbContext.Schema;

    // Định danh nhóm Swagger KHÔNG nằm ở đây mà ở Presentation/IdentityApiGroup.cs — nó là từ vựng
    // HTTP, thuộc tầng sở hữu HTTP. Bề mặt DI chỉ nói về việc ráp dịch vụ.

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
    {
        // Cấu hình Npgsql + bảng lịch sử migration nằm ở IdentityDbContextOptions — dùng chung với
        // design-time factory để hai đường không lệch nhau.
        services.AddDbContext<IdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));

        // Nguồn thật của ma trận quyền cho tầng 2 (C5): đọc role_permissions. SharedKernel chỉ biết interface.
        services.AddScoped<IRolePermissionSource, RolePermissionSource>();

        // Viên gạch chung của khối D (D0). Cả hai stateless → singleton. JwtAccessTokenIssuer đọc IOptions<JwtOptions>
        // do HOST đăng ký sau khi validate + giải fallback deploy/.env — module KHÔNG bind lại section "Jwt".
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        // Validator của module (D1–D3). Auto-validation của MVC bật MỘT lần ở host, không ở đây.
        services.AddValidatorsFromAssembly(typeof(IdentityModuleExtensions).Assembly, ServiceLifetime.Singleton);

        // Luồng auth (D1+). Scoped vì store dùng IdentityDbContext.
        services.AddScoped<IIdentityUserStore, IdentityUserStore>();
        services.AddScoped<IEmailVerificationStore, EmailVerificationStore>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<LoginService>();
        services.AddScoped<MeQuery>();
        services.AddScoped<SessionService>();
        return services;
    }

    /// <summary>
    /// Gửi mail xác minh qua SMTP (Đ-D9). Tách khỏi <see cref="AddIdentityModule"/> vì cần cấu hình + môi trường của
    /// host, còn <see cref="AddIdentityModule"/> được test dựng trần chỉ với chuỗi kết nối.
    ///
    /// Cấu hình thiếu ngoài Development thì ném NGAY lúc gọi — không đợi mail đầu tiên mới biết (cùng tinh thần
    /// RequireConnectionString). Host gọi sau RequireJwtOptions để thông báo lỗi JWT/DB vẫn đến trước.
    /// </summary>
    public static IServiceCollection AddIdentityEmail(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var smtp = SmtpOptions.FromConfiguration(configuration, environment);
        services.AddSingleton<IEmailSender>(sp =>
            new SmtpEmailSender(smtp, sp.GetRequiredService<ILogger<SmtpEmailSender>>()));
        return services;
    }

    /// <summary>
    /// Chạy ở hook <c>--migrate</c> (service `migrate` one-shot lúc deploy), KHÔNG chạy khi api khởi động.
    /// Đúng thứ tự: apply migration → seed dữ liệu nền → kiểm tra vai trò hệ thống (Mục 5.4, 5.5).
    /// Migrate trước, seed sau — đảo lại thì seed vào bảng chưa tồn tại và chết bằng lỗi Postgres thô.
    ///
    /// Ngoại lệ PHẢI thoát ra ngoài, người gọi không được bọc try/catch: thiếu vai trò hệ thống thì bước
    /// deploy phải đỏ, không được chạy tiếp trên dữ liệu nền hỏng.
    /// </summary>
    public static async Task MigrateIdentityModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(ct);
        await IdentitySeeder.SeedAsync(db, ct);   // seed + kiểm tra vai trò hệ thống (A4, A5)
    }
}
