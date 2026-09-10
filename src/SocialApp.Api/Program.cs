using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;
using SocialApp.Api.Controllers;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Presentation;
using SocialApp.SharedKernel.DependencyInjection;

// Service `migrate` (one-shot, chạy ở bước deploy) gọi với cờ --migrate: apply EF migration cho
// mọi module context rồi thoát 0. Lọc cờ khỏi args vì CommandLine config provider không hiểu cờ
// không có giá trị.
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
var postgres = RequireConnectionString("Postgres",
    "Host=localhost;Port=5432;Database=socialapp;Username=socialapp;Password=socialapp");
var redis = RequireConnectionString("Redis", "localhost:6379");

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
// Development vẫn được rơi về giá trị local để chạy `dotnet run` không cần cấu hình gì.
string RequireConnectionString(string name, string developmentFallback)
{
    var value = builder.Configuration.GetConnectionString(name);
    if (!string.IsNullOrWhiteSpace(value))
        return value;

    if (builder.Environment.IsDevelopment())
        return developmentFallback;

    throw new InvalidOperationException(
        $"Thiếu ConnectionStrings:{name} ở môi trường '{builder.Environment.EnvironmentName}'. "
      + $"Đặt biến môi trường ConnectionStrings__{name} trong deploy/.env rồi deploy lại. "
      + "App từ chối khởi động thay vì chạy tiếp với cấu hình thiếu.");
}

// --- Module Identity: DbContext riêng, schema "identity" (ADR-001) ---
builder.Services.AddIdentityModule(postgres);

builder.Services.AddHealthChecks()
    .AddNpgSql(postgres, name: "postgres", tags: ["ready"])
    .AddRedis(redis, name: "redis", tags: ["ready"]);

var app = builder.Build();

// Thoát ngay sau khi migrate: container `migrate` không phục vụ request nào.
if (isMigrate)
{
    await app.Services.MigrateIdentityModuleAsync();
    Console.WriteLine($"[migrate] Đã áp dụng migration cho schema \"{IdentityModuleExtensions.Schema}\". Thoát 0.");
    return;
}

app.UseSerilogRequestLogging();
app.UseSharedKernel();

// GĐ1 chèn app.UseAuthentication() + app.UseAuthorization() vào ĐÂY — trước rate limiter, xem
// UseSharedKernelRateLimiter: limiter phân vùng theo user nên phải chạy sau khi User được dựng.
app.UseSharedKernelRateLimiter();

// Swagger bật ở Development + Staging (để demo/test trên staging); TẮT ở Production.
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

// /health/live: app còn sống (không kiểm phụ thuộc). /health/ready: sẵn sàng nhận tải (DB+Redis OK).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

// Cho phép WebApplicationFactory<Program> trong IntegrationTests tham chiếu Program.
public partial class Program;
