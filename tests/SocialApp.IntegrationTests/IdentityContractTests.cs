using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using SocialApp.Modules.Identity.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Trọng tài của hợp đồng API module Identity: so file hợp đồng
/// <c>Modules/Identity/Presentation/identity-v1.yaml</c> (điều đã thỏa thuận ở cổng mở) với
/// <c>/swagger/identity-v1/swagger.json</c> sinh từ code (điều code thật sự làm).
///
/// Đây là thứ THAY CHO việc "đối chiếu Swagger với stub bằng mắt" ở cổng đóng GĐ1 (Mục 11). Hai
/// nguồn sự thật mà đối chiếu thủ công thì sớm muộn cũng lệch; có test thì lệch là CI đỏ.
///
/// Cố ý KHÔNG so description/example: sửa một chữ mô tả mà đỏ test thì cả nhóm sẽ học cách phớt lờ
/// nó. Chỉ so phần thật sự là hợp đồng — tập (path × method), tập status code, và required field.
/// </summary>
[Trait("Category", "Contract")]
public sealed class IdentityContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Contract dùng path tương đối với server URL; Swagger runtime dùng path tuyệt đối.</summary>
    private const string BasePath = "/api/v1";

    private static readonly string ContractPath =
        Path.Combine(AppContext.BaseDirectory, "Contracts", "identity-v1.yaml");

    /// <summary>
    /// Status code do middleware sinh, không phải quyết định của từng action: 429 từ rate limiter,
    /// 500 từ GlobalExceptionHandler. Hợp đồng ghi chúng vì client cần biết; Swagger không thấy
    /// chúng vì không action nào khai [ProducesResponseType]. Trừ khỏi cả hai vế để test không ép
    /// khối D rắc [ProducesResponseType(429)] lên từng action cho có.
    /// </summary>
    private static readonly int[] CrossCuttingStatusCodes = [429, 500];

    private static OpenApiDocument ReadContract()
    {
        Assert.True(File.Exists(ContractPath),
            $"Không thấy file hợp đồng ở {ContractPath}. Kiểm tra <Content Include=... Link=Contracts\\> "
          + "trong SocialApp.IntegrationTests.csproj.");

        var doc = new OpenApiStringReader().Read(File.ReadAllText(ContractPath), out var diagnostic);
        Assert.True(diagnostic.Errors.Count == 0,
            "File hợp đồng không parse được: " + string.Join(" | ", diagnostic.Errors.Select(e => e.Message)));
        return doc;
    }

    private async Task<OpenApiDocument> ReadRuntimeSwaggerAsync()
    {
        // Swagger chỉ bật ở Development + Staging (TẮT ở Production — AGENTS.md Mục 9), nên phải
        // nói rõ môi trường thay vì dựa vào mặc định của WebApplicationFactory.
        //
        // Chọn Development chứ không phải Staging: từ khi Program.cs fail-fast khi thiếu chuỗi kết
        // nối, Staging trong test sẽ chết ngay lúc khởi động (đúng như thiết kế — xem
        // StartupConfigurationTests). Development đọc appsettings.Development.json nên có cấu hình
        // thật, và Swagger vẫn bật.
        var client = factory
            .WithWebHostBuilder(b => b.UseEnvironment("Development"))
            .CreateClient();

        var json = await client.GetStringAsync($"/swagger/{IdentityApiGroup.Name}/swagger.json");
        var doc = new OpenApiStringReader().Read(json, out var diagnostic);
        Assert.True(diagnostic.Errors.Count == 0,
            "Swagger runtime không parse được: " + string.Join(" | ", diagnostic.Errors.Select(e => e.Message)));
        return doc;
    }

    /// <summary>Tập "METHOD path" đã chuẩn hóa để hai bên so được với nhau.</summary>
    private static HashSet<string> Operations(OpenApiDocument doc, bool stripBasePath) =>
        doc.Paths
            .SelectMany(p => p.Value.Operations.Select(op =>
                $"{op.Key.ToString().ToUpperInvariant()} {Normalize(p.Key, stripBasePath)}"))
            .ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string path, bool stripBasePath) =>
        stripBasePath && path.StartsWith(BasePath, StringComparison.Ordinal)
            ? path[BasePath.Length..]
            : path;

    private static Dictionary<string, HashSet<int>> StatusCodes(OpenApiDocument doc, bool stripBasePath) =>
        doc.Paths
            .SelectMany(p => p.Value.Operations.Select(op => (
                Key: $"{op.Key.ToString().ToUpperInvariant()} {Normalize(p.Key, stripBasePath)}",
                Codes: op.Value.Responses.Keys
                    .Where(k => int.TryParse(k, out _))
                    .Select(int.Parse)
                    .Except(CrossCuttingStatusCodes)
                    .ToHashSet())))
            .ToDictionary(x => x.Key, x => x.Codes, StringComparer.Ordinal);

    /// <summary>
    /// Chiều 1 — code KHÔNG được lộ ra thứ hợp đồng chưa ghi.
    ///
    /// Bắt đúng cái sai hay xảy ra nhất: thêm endpoint (hoặc thêm một status code) rồi quên cập nhật
    /// hợp đồng, khiến lane frontend codegen ra type thiếu. Chạy xanh được NGAY từ bây giờ vì chưa
    /// có controller nào — và giữ xanh là trách nhiệm của mọi commit sau.
    /// </summary>
    [Fact]
    public async Task Runtime_must_not_expose_anything_outside_the_contract()
    {
        var contract = ReadContract();
        var runtime = await ReadRuntimeSwaggerAsync();

        var undocumented = Operations(runtime, stripBasePath: true)
            .Except(Operations(contract, stripBasePath: false))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(undocumented.Count == 0,
            "Code lộ endpoint chưa có trong hợp đồng — cập nhật identity-v1.yaml TRONG CÙNG COMMIT:\n  "
          + string.Join("\n  ", undocumented));

        var contractCodes = StatusCodes(contract, stripBasePath: false);
        var extraCodes = StatusCodes(runtime, stripBasePath: true)
            .Where(kv => contractCodes.ContainsKey(kv.Key))
            .Select(kv => (kv.Key, Extra: kv.Value.Except(contractCodes[kv.Key]).ToList()))
            .Where(x => x.Extra.Count > 0)
            .Select(x => $"{x.Key}: {string.Join(", ", x.Extra)}")
            .ToList();

        Assert.True(extraCodes.Count == 0,
            "Code trả status code chưa ghi trong hợp đồng:\n  " + string.Join("\n  ", extraCodes));
    }

    /// <summary>
    /// Chiều 2 — hợp đồng phải được hiện thực đủ.
    ///
    /// Đang Skip vì khối D chưa viết controller nào: hợp đồng có 6 endpoint, Swagger runtime có 0.
    /// Đã chạy thử một lần không Skip để xác nhận nó đỏ đúng lý do (thiếu đủ 6 operation), rồi mới
    /// gắn Skip lại — một test chưa bao giờ đỏ thì không chứng minh được điều gì.
    ///
    /// GỠ SKIP khi khối D GĐ1 ráp xong 6 endpoint auth. Từ lúc đó nó là cổng chặn hai chiều.
    /// </summary>
    [Fact(Skip = "Gỡ Skip khi khối D GĐ1 ráp xong 6 endpoint auth trong Modules/Identity/Presentation")]
    public async Task Contract_must_be_fully_implemented()
    {
        var contract = ReadContract();
        var runtime = await ReadRuntimeSwaggerAsync();

        var missing = Operations(contract, stripBasePath: false)
            .Except(Operations(runtime, stripBasePath: true))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Hợp đồng có endpoint mà code chưa hiện thực:\n  " + string.Join("\n  ", missing));

        var runtimeCodes = StatusCodes(runtime, stripBasePath: true);
        var missingCodes = StatusCodes(contract, stripBasePath: false)
            .Select(kv => (kv.Key, Missing: kv.Value.Except(runtimeCodes.GetValueOrDefault(kv.Key, [])).ToList()))
            .Where(x => x.Missing.Count > 0)
            .Select(x => $"{x.Key}: {string.Join(", ", x.Missing)}")
            .ToList();

        Assert.True(missingCodes.Count == 0,
            "Hợp đồng ghi status code mà action chưa khai [ProducesResponseType]:\n  "
          + string.Join("\n  ", missingCodes));

        var requiredMismatch = contract.Paths
            .SelectMany(p => p.Value.Operations.Select(op => (
                Key: $"{op.Key.ToString().ToUpperInvariant()} {p.Key}",
                Contract: RequiredRequestFields(op.Value, contract))))
            .Where(x => x.Contract.Count > 0)
            .Select(x => (x.Key, x.Contract, Runtime: RuntimeRequiredFields(runtime, x.Key)))
            .Where(x => !x.Contract.SetEquals(x.Runtime))
            .Select(x => $"{x.Key}: hợp đồng [{string.Join(", ", x.Contract.Order())}] "
                       + $"vs code [{string.Join(", ", x.Runtime.Order())}]")
            .ToList();

        Assert.True(requiredMismatch.Count == 0,
            "Required field của request body lệch giữa hợp đồng và code:\n  "
          + string.Join("\n  ", requiredMismatch));
    }

    private static HashSet<string> RuntimeRequiredFields(OpenApiDocument runtime, string contractKey)
    {
        var match = runtime.Paths
            .SelectMany(p => p.Value.Operations.Select(op => (
                Key: $"{op.Key.ToString().ToUpperInvariant()} {Normalize(p.Key, stripBasePath: true)}",
                Operation: op.Value)))
            .FirstOrDefault(x => x.Key == contractKey);

        return match.Operation is null ? [] : RequiredRequestFields(match.Operation, runtime);
    }

    /// <summary>
    /// Required field của request body JSON. Tự phân giải <c>$ref</c> thay vì tin vào reader: hai
    /// document đọc từ hai định dạng khác nhau (YAML và JSON) nên không đảm bảo ref được nội suy
    /// giống hệt nhau.
    /// </summary>
    private static HashSet<string> RequiredRequestFields(OpenApiOperation operation, OpenApiDocument doc)
    {
        if (operation.RequestBody?.Content.TryGetValue("application/json", out var media) != true)
            return [];

        var schema = Resolve(media!.Schema, doc);
        return schema?.Required?.ToHashSet(StringComparer.Ordinal) ?? [];
    }

    private static OpenApiSchema? Resolve(OpenApiSchema? schema, OpenApiDocument doc)
    {
        if (schema?.Reference is null)
            return schema;

        return doc.Components?.Schemas?.TryGetValue(schema.Reference.Id, out var resolved) == true
            ? resolved
            : schema;
    }
}
