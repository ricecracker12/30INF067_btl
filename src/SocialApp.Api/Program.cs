using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;
using SocialApp.Modules.Identity.DependencyInjection;
using SocialApp.Modules.Identity.Infrastructure;
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
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Health checks: /health/ready kiểm tra Postgres + Redis (tag "ready") ---
var postgres = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=socialapp;Username=socialapp;Password=socialapp";
var redis = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

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
    Console.WriteLine($"[migrate] Đã áp dụng migration cho schema \"{IdentityDbContext.Schema}\". Thoát 0.");
    return;
}

app.UseSerilogRequestLogging();
app.UseSharedKernel();

// Swagger bật ở Development + Staging (để demo/test trên staging); TẮT ở Production.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// /health/live: app còn sống (không kiểm phụ thuộc). /health/ready: sẵn sàng nhận tải (DB+Redis OK).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

// Cho phép WebApplicationFactory<Program> trong IntegrationTests tham chiếu Program.
public partial class Program;
