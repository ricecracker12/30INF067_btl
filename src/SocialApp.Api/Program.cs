using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;
using SocialApp.Api.Controllers;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Presentation;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Configuration;
using SocialApp.SharedKernel.DependencyInjection;

// Service `migrate` (one-shot, chạy ở bước deploy) gọi với cờ --migrate: apply EF migration cho
// mọi module context, nạp dữ liệu nền + kiểm tra vai trò hệ thống, rồi thoát 0 (lỗi thì thoát khác 0).
// Lọc cờ khỏi args vì CommandLine config provider không hiểu cờ không có giá trị.
var isMigrate = args.Contains("--migrate");
var hostArgs = args.Where(a => a != "--migrate").ToArray();

var builder = WebApplication.CreateBuilder(hostArgs);

// --- Logging: Serilog JSON (compact) + correlation id qua LogContext ---
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

// --- Hạ tầng dùng chung: RFC 7807 + exception handler + rate limit ---
builder.Services.AddSharedKernel();

// --- MVC controllers + quy ước JSON (enum dạng chuỗi; field lạ -> 400) ---
// AddApplicationPart: mỗi module SỞ HỮU tầng HTTP của mình (Modules/<Module>/Presentation), host
// chỉ nạp assembly vào. Một dòng cho mỗi module, cố ý để lộ ra đây thay vì quét assembly tự động —
// quét tự động thì thêm/bớt module không hiện ra ở đâu cả.
builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(IdentityApiGroup).Assembly)
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });

// --- Validation: FluentValidation chạy trước action, lỗi ra 400 ValidationProblemDetails ---
// MỘT lần cho cả host: đây là cấu hình MVC toàn cục. Module chỉ đăng ký validator của mình (AddIdentityModule) —
// mỗi module tự gọi dòng này là lỗi validate bị lặp.
// Tắt DataAnnotations: [Required] trên DTO chỉ để Swagger ghi required. Để nó validate nữa thì (đã kiểm) lỗi của nó và
// của FluentValidation dồn vào CÙNG một key PascalCase "Email", kèm thông điệp tiếng Anh — hợp đồng cần key `email`.
builder.Services.AddFluentValidationAutoValidation(o => o.DisableDataAnnotationsValidation = true);

// Key trong `errors` phải camelCase như hợp đồng (`password`, không `Password`). Static toàn cục của FluentValidation.
ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
    member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);

// --- Swagger tách theo module: mỗi module một trang, không dồn 7 module vào một chỗ ---
// Tên nhóm do chính module công bố ở tầng Presentation (vd IdentityApiGroup) và trùng tên file hợp
// đồng Modules/<Module>/Presentation/<nhóm>.yaml — nhờ vậy IdentityContractTests so được hai bên.
var apiGroups = new[]
{
    (Name: PingController.ApiGroup, Title: "Platform"),
    (Name: IdentityApiGroup.Name, Title: IdentityApiGroup.Title),
};

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    foreach (var (name, title) in apiGroups)
        c.SwaggerDoc(name, new OpenApiInfo { Title = title, Version = "v1" });

    // Lọc theo GroupName. HỆ QUẢ: controller KHÔNG khai [ApiExplorerSettings] có GroupName sẽ bị
    // loại khỏi mọi trang — không exception, không log, endpoint chỉ đơn giản biến mất khỏi tài
    // liệu. PresentationBoundaryTests có một test bắt đúng chuyện này.
    c.DocInclusionPredicate((docName, api) => api.GroupName == docName);
});

// --- Health checks: /health/ready kiểm tra Postgres + Redis (tag "ready") ---
// Fallback Development của Postgres: compose dev qua localhost, mật khẩu đọc từ deploy/.env — cùng file
// compose dev dùng, không giữ bản sao ghi cứng trong repo (xem DevEnvFile).
var postgres = RequireConnectionString("Postgres",
    () => DevEnvFile.LocalPostgresConnectionString(builder.Environment.ContentRootPath));
var redis = RequireConnectionString("Redis", () => "localhost:6379");

// JWT kiểm SAU chuỗi kết nối, không phải tùy ý: StartupConfigurationTests dựng app thiếu cả hai và khẳng
// định thông báo nêu ConnectionStrings:Postgres. Kiểm JWT trước thì test đó đỏ dù hành vi đúng.
var jwt = RequireJwtOptions();

// Thiếu chuỗi kết nối thì CHẾT NGAY TẠI ĐÂY, kèm thông báo nêu đúng key và đúng chỗ sửa.
//
// Phải kiểm cả chuỗi RỖNG chứ không chỉ null: appsettings.json khai "ConnectionStrings:Postgres": ""
// (khai sẵn key cho rõ hình dạng config) nên GetConnectionString trả "" — toán tử `??` không bao giờ
// kích hoạt. Hai kiểu hỏng câm đã cân nhắc rồi loại bỏ:
//   - Để nguyên "" đi tiếp  -> AddNpgSql ném ArgumentNullException, thông báo chỉ vào HEALTH CHECK
//                              chứ không chỉ vào config. Dò nhầm chỗ.
//   - Rơi về localhost      -> app khởi động BÌNH THƯỜNG rồi hỏng ngầm: trong container api,
//                              localhost:5432 không có gì, /health/ready đỏ sau ~95 giây, mà Caddy
//                              vẫn proxy traffic vào app hỏng.
// Development: không đặt biến thì dựng chuỗi local — Postgres lấy mật khẩu từ deploy/.env và BẮT BUỘC có
// file (DevEnvFile ném, nêu đúng chỗ sửa), cùng tinh thần fail-fast ở trên. Test dựng app phải tự khai
// chuỗi kết nối (ApiFactory). Fallback là hàm để chỉ được tính khi thật sự ở Development: ngoài
// Development không đụng tới file nào.
string RequireConnectionString(string name, Func<string> developmentFallback)
{
    var value = builder.Configuration.GetConnectionString(name);
    if (!string.IsNullOrWhiteSpace(value))
        return value;

    if (builder.Environment.IsDevelopment())
        return developmentFallback();

    throw new InvalidOperationException(
        $"Thiếu ConnectionStrings:{name} ở môi trường '{builder.Environment.EnvironmentName}'. "
      + $"Đặt biến môi trường ConnectionStrings__{name} trong deploy/.env rồi deploy lại. "
      + "App từ chối khởi động thay vì chạy tiếp với cấu hình thiếu.");
}

// Cấu hình JWT (tầng 1, Mục 6.1) — cùng tinh thần RequireConnectionString: thiếu hoặc yếu thì chết ngay.
// KHÔNG có khóa mặc định ở bất kỳ môi trường nào. Development không đặt Jwt__SigningKey thì đọc từ
// deploy/.env (DevEnvFile), thiếu ở đó cũng chết. Issuer/Audience/AccessTokenSeconds không bí mật nên nằm
// trong appsettings.json; chỉ SigningKey nằm ở biến môi trường.
//
// Lưu ý: --migrate cũng đi qua đây (đọc cấu hình trước nhánh isMigrate). Service migrate của staging đã nạp
// env_file ./.env nên có khóa. Không tách nhánh riêng — hai hình dạng app là thêm một chỗ lệch.
JwtOptions RequireJwtOptions()
{
    var bound = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

    var signingKey = bound.SigningKey;
    if (string.IsNullOrWhiteSpace(signingKey) && builder.Environment.IsDevelopment())
        signingKey = DevEnvFile.LocalJwtSigningKey(builder.Environment.ContentRootPath) ?? "";

    var problems = new List<string>();
    if (Encoding.UTF8.GetByteCount(signingKey) < JwtOptions.MinSigningKeyBytes)
        problems.Add($"Jwt:SigningKey trống hoặc ngắn hơn {JwtOptions.MinSigningKeyBytes} byte (HS256 cần khóa ≥ 256 bit)");
    if (string.IsNullOrWhiteSpace(bound.Issuer))
        problems.Add("Jwt:Issuer trống");
    if (string.IsNullOrWhiteSpace(bound.Audience))
        problems.Add("Jwt:Audience trống");
    if (bound.AccessTokenSeconds <= 0)
        problems.Add("Jwt:AccessTokenSeconds phải lớn hơn 0");
    if (bound.RefreshTokenDays <= 0)
        problems.Add("Jwt:RefreshTokenDays phải lớn hơn 0");

    if (problems.Count > 0)
        throw new InvalidOperationException(
            $"Cấu hình JWT không hợp lệ ở môi trường '{builder.Environment.EnvironmentName}': {string.Join("; ", problems)}. "
          + "Đặt biến môi trường Jwt__SigningKey (sinh bằng: openssl rand -base64 48) trong deploy/.env rồi chạy lại. "
          + "App từ chối khởi động thay vì chạy tiếp với cấu hình thiếu.");

    return new JwtOptions
    {
        SigningKey = signingKey,
        Issuer = bound.Issuer,
        Audience = bound.Audience,
        AccessTokenSeconds = bound.AccessTokenSeconds,
        RefreshTokenDays = bound.RefreshTokenDays,
    };
}

// --- Module Identity: DbContext riêng, schema "identity" (ADR-001) ---
builder.Services.AddIdentityModule(postgres);

// Mail xác minh (Đ-D9). Development không đặt gì → Mailpit localhost:1025 + link http://localhost:3000; ngoài
// Development thiếu Smtp:Host/Port/From hoặc Frontend:BaseUrl thì chết ngay tại đây. KHÔNG đọc từ deploy/.env: file đó
// mang giá trị staging.
builder.Services.AddIdentityEmail(builder.Configuration, builder.Environment);

// --- Tầng 1 (AuthN, Mục 6.1): JWT Bearer ---
// JwtOptions đã validate và đã giải fallback deploy/.env: phát token (D3) và TTL revoked:user (D8) lấy
// IOptions<JwtOptions> từ đây, KHÔNG đọc lại section "Jwt" — đọc lại thì mất khóa lấy từ deploy/.env.
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(jwt));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Giữ nguyên tên claim ngắn "sub"/"role". Mặc định handler đổi "role" thành URI của
        // ClaimTypes.Role → FindFirstValue("role") trả null → PermissionHandler từ chối cả Admin.
        // KHÔNG dùng cách xóa DefaultInboundClaimTypeMap: đó là static toàn cục, ảnh hưởng mọi handler.
        o.MapInboundClaims = false;

        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],   // chặn đổi thuật toán trong header
            // Mặc định 5 phút = access sống 20 phút. Đọc hằng số, không ghi tay: TTL revoked:user cộng CÙNG số này (Đ-D4).
            ClockSkew = TimeSpan.FromSeconds(JwtOptions.ClockSkewSeconds),
            NameClaimType = JwtClaims.Sub,
            RoleClaimType = JwtClaims.Role,
        };

        // D8 gắn OnTokenValidated (ITokenRevocationStore) vào đúng chỗ này.
    });

// --- Tầng 2 (RBAC, Mục 6.2): [RequirePermission] + fallback policy default deny ---
builder.Services.AddSharedKernelAuthorization();

builder.Services.AddHealthChecks()
    .AddNpgSql(postgres, name: "postgres", tags: ["ready"])
    .AddRedis(redis, name: "redis", tags: ["ready"]);

var app = builder.Build();

// Thoát ngay sau khi migrate: container `migrate` không phục vụ request nào.
// KHÔNG bọc try/catch: lỗi migration hay thiếu vai trò hệ thống phải làm process thoát khác 0, để
// `set -e` ở CD dừng lại TRƯỚC `up -d` thay vì bật api trên dữ liệu nền hỏng.
if (isMigrate)
{
    await app.Services.MigrateIdentityModuleAsync();
    Console.WriteLine($"[migrate] Đã áp dụng migration, nạp dữ liệu nền và kiểm tra vai trò hệ thống cho schema \"{IdentityModuleExtensions.Schema}\". Thoát 0.");
    return;
}

app.UseSerilogRequestLogging();
app.UseSharedKernel();

// Swagger bật ở Development + Staging (để demo/test trên staging); TẮT ở Production.
// PHẢI đứng TRƯỚC UseAuthorization: fallback policy áp cho mọi request mà middleware authorization nhìn
// thấy, kể cả request do middleware đứng sau phục vụ (Swagger không phải endpoint) → swagger.json 401 →
// cổng hợp đồng API đỏ.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // Một mục trong dropdown cho mỗi module. /swagger/identity-v1/swagger.json cũng chính là
        // thứ IdentityContractTests tải về để so với file hợp đồng.
        foreach (var (name, title) in apiGroups)
            c.SwaggerEndpoint($"/swagger/{name}/swagger.json", title);
    });
}

// Tầng 1 + tầng 2, TRƯỚC rate limiter — xem UseSharedKernelRateLimiter. Không test tự động nào bắt được
// thứ tự này (B.9 điều 3): đổi chỗ ba dòng dưới phải qua code review.
app.UseAuthentication();
app.UseAuthorization();
app.UseSharedKernelRateLimiter();

// /health/live: app còn sống (không kiểm phụ thuộc). /health/ready: sẵn sàng nhận tải (DB+Redis OK).
// AllowAnonymous: healthcheck của Docker/Caddy không có token — thiếu dòng này thì fallback policy trả 401
// và container bị báo unhealthy (SmokeEndpointsTests.Health_ready_khong_can_token).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapControllers();

app.Run();

// Cho phép WebApplicationFactory<Program> trong IntegrationTests tham chiếu Program.
public partial class Program;
