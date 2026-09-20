using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Trọng tài của hợp đồng API, dùng chung cho MỌI module: so file hợp đồng
/// <c>Modules/&lt;Module&gt;/Presentation/&lt;nhóm&gt;.yaml</c> (điều đã thỏa thuận ở cổng mở) với
/// <c>/swagger/&lt;nhóm&gt;/swagger.json</c> sinh từ code (điều code thật sự làm).
///
/// Đây là thứ THAY CHO việc "đối chiếu Swagger với stub bằng mắt" ở cổng đóng GĐ1 (Mục 11). Hai nguồn sự thật mà đối
/// chiếu thủ công thì sớm muộn cũng lệch; có test thì lệch là CI đỏ.
///
/// Cố ý KHÔNG so description/example: sửa một chữ mô tả mà đỏ test thì cả nhóm sẽ học cách phớt lờ nó. Chỉ so phần thật
/// sự là hợp đồng — tập (path × method), tập status code, và required field.
///
/// <b>Vì sao là lớp cơ sở chứ không chép ba bản</b> (B4, chốt khi thi công): B.4 đề nghị "chép <c>IdentityContractTests</c>
/// cho từng module". Chép ba bản nghĩa là BA CHỖ phải sửa khi logic so sánh đổi — và nó <i>sẽ</i> đổi: GĐ5 thêm module
/// thứ tư, <see cref="CrossCuttingStatusCodes"/> sẽ phải nhận thêm mã. Hai bản lệch nhau âm thầm là cách cổng hợp đồng
/// chết mà không ai báo tang.
///
/// <b>Ba điều bắt buộc ở LỚP CON</b>, không được quên cái nào:
/// <list type="number">
/// <item><c>[Trait("Category","Contract")]</c> đặt trên TỪNG lớp con, không chỉ ở đây. Bước CI lọc theo trait, và một
/// trait không được phát hiện nghĩa là cổng chạy thiếu test mà vẫn xanh.</item>
/// <item><c>IClassFixture&lt;ApiFactory&gt;</c> cũng khai ở lớp con — xUnit tạo fixture theo lớp CỤ THỂ.</item>
/// <item>Hai thuộc tính abstract bên dưới, và tên nhóm lấy từ hằng <c>&lt;Module&gt;ApiGroup.Name</c> chứ không gõ chuỗi:
/// tên đó phải khớp cả ba chỗ (<c>[ApiExplorerSettings]</c> · <c>SwaggerDoc</c> · tên file yaml).</item>
/// </list>
/// </summary>
public abstract class ContractTestsBase(ApiFactory factory)
{
    /// <summary>Contract dùng path tương đối với server URL; Swagger runtime dùng path tuyệt đối.</summary>
    private const string BasePath = "/api/v1";

    /// <summary>
    /// Status code do middleware sinh, không phải quyết định của từng action: 429 từ rate limiter, 500 từ
    /// <c>GlobalExceptionHandler</c>. Hợp đồng ghi chúng vì client cần biết; Swagger không thấy chúng vì không action
    /// nào khai <c>[ProducesResponseType]</c>. Trừ khỏi cả hai vế để test không ép khối D rắc
    /// <c>[ProducesResponseType(429)]</c> lên từng action cho có.
    /// </summary>
    private static readonly int[] CrossCuttingStatusCodes = [429, 500];

    /// <summary>Tên file trong thư mục <c>Contracts/</c> của output test, ví dụ <c>content-v1.yaml</c>.</summary>
    protected abstract string ContractFileName { get; }

    /// <summary>Tên nhóm Swagger, lấy từ hằng <c>&lt;Module&gt;ApiGroup.Name</c> — trùng tên file bỏ đuôi.</summary>
    protected abstract string SwaggerGroupName { get; }

    private OpenApiDocument ReadContract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Contracts", ContractFileName);

        Assert.True(File.Exists(path),
            $"Không thấy file hợp đồng ở {path}. Kiểm tra <Content Include=... Link=Contracts\\> "
          + "trong SocialApp.IntegrationTests.csproj — mỗi module một dòng.");

        var doc = new OpenApiStringReader().Read(File.ReadAllText(path), out var diagnostic);
        Assert.True(diagnostic.Errors.Count == 0,
            $"File hợp đồng {ContractFileName} không parse được: "
          + string.Join(" | ", diagnostic.Errors.Select(e => e.Message)));
        return doc;
    }

    private async Task<OpenApiDocument> ReadRuntimeSwaggerAsync()
    {
        // Môi trường (Development, vì Swagger TẮT ở Production — AGENTS.md Mục 9) và chuỗi kết nối tường minh đều do
        // ApiFactory khai, nên test này chạy được trên CI không có deploy/.env. ApiFactory cố ý KHÔNG tới được Postgres
        // và cố ý không có khóa R2 (Q-C1): controller nào chạm DB/R2 lúc khởi động sẽ làm cổng này đỏ — sửa controller,
        // đừng sửa ApiFactory.
        var client = factory.CreateClient();

        var json = await client.GetStringAsync($"/swagger/{SwaggerGroupName}/swagger.json");
        var doc = new OpenApiStringReader().Read(json, out var diagnostic);
        Assert.True(diagnostic.Errors.Count == 0,
            $"Swagger runtime của nhóm {SwaggerGroupName} không parse được: "
          + string.Join(" | ", diagnostic.Errors.Select(e => e.Message)));
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
    /// Bắt đúng cái sai hay xảy ra nhất: thêm endpoint (hoặc thêm một status code) rồi quên cập nhật hợp đồng, khiến
    /// lane frontend codegen ra type thiếu.
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
            $"Code lộ endpoint chưa có trong hợp đồng — cập nhật {ContractFileName} TRONG CÙNG COMMIT:\n  "
          + string.Join("\n  ", undocumented));

        var contractCodes = StatusCodes(contract, stripBasePath: false);
        var extraCodes = StatusCodes(runtime, stripBasePath: true)
            .Where(kv => contractCodes.ContainsKey(kv.Key))
            .Select(kv => (kv.Key, Extra: kv.Value.Except(contractCodes[kv.Key]).ToList()))
            .Where(x => x.Extra.Count > 0)
            .Select(x => $"{x.Key}: {string.Join(", ", x.Extra)}")
            .ToList();

        Assert.True(extraCodes.Count == 0,
            $"Code trả status code chưa ghi trong {ContractFileName}:\n  " + string.Join("\n  ", extraCodes));
    }

    /// <summary>
    /// Chiều 2 — hợp đồng phải được hiện thực đủ: đủ (path × method), đủ status code trên <c>[ProducesResponseType]</c>,
    /// required field của request body khớp.
    ///
    /// Thêm operation hay status code vào yaml mà chưa hiện thực là CI đỏ — không gắn <c>Skip</c> để né.
    /// </summary>
    [Fact]
    public async Task Contract_must_be_fully_implemented()
    {
        var contract = ReadContract();
        var runtime = await ReadRuntimeSwaggerAsync();

        var missing = Operations(contract, stripBasePath: false)
            .Except(Operations(runtime, stripBasePath: true))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{ContractFileName} có endpoint mà code chưa hiện thực:\n  " + string.Join("\n  ", missing));

        var runtimeCodes = StatusCodes(runtime, stripBasePath: true);
        var missingCodes = StatusCodes(contract, stripBasePath: false)
            .Select(kv => (kv.Key, Missing: kv.Value.Except(runtimeCodes.GetValueOrDefault(kv.Key, [])).ToList()))
            .Where(x => x.Missing.Count > 0)
            .Select(x => $"{x.Key}: {string.Join(", ", x.Missing)}")
            .ToList();

        Assert.True(missingCodes.Count == 0,
            $"{ContractFileName} ghi status code mà action chưa khai [ProducesResponseType]:\n  "
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
            $"Required field của request body lệch giữa {ContractFileName} và code:\n  "
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
    /// Required field của request body JSON. Tự phân giải <c>$ref</c> thay vì tin vào reader: hai document đọc từ hai
    /// định dạng khác nhau (YAML và JSON) nên không đảm bảo ref được nội suy giống hệt nhau.
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
