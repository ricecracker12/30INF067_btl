using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Canh chính cổng hợp đồng: mọi hợp đồng trong repo đều có người so. <see cref="ContractTestsBase"/> chỉ so được hợp đồng
/// mà ai đó nhớ nối vào — module mới thả <c>&lt;nhóm&gt;-v1.yaml</c> vào <c>Presentation/</c> (codegen FE nhặt ngay theo glob)
/// mà quên dòng <c>Content Include</c> trong csproj hoặc quên lớp <c>…ContractTests</c> thì KHÔNG test nào so hợp đồng đó, và
/// CI vẫn xanh. Chỗ hở này lộ ra ở thử đỏ của B5 (GĐ4), bịt sau tự rà cuối khối B+C+D.
///
/// Mang trait <c>Contract</c> để chạy NGAY trong bước "API contract (CI GATE)" — cùng chỗ với thứ nó canh.
/// Glob nguồn giống <c>src/frontend/scripts/gen-api.mjs</c>: <c>Modules/*/Presentation/*-v1.yaml</c>.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ContractGateCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SocialApp.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, $"Không tìm thấy SocialApp.sln từ {AppContext.BaseDirectory} đi lên");
        return dir!.FullName;
    }

    /// <summary>Tên file hợp đồng trong source: <c>src/backend/Modules/&lt;Module&gt;/Presentation/*-v1.yaml</c>.</summary>
    private static SortedSet<string> SourceContracts()
    {
        var modules = Path.Combine(RepoRoot(), "src", "backend", "Modules");
        // Module khung chưa có tầng HTTP (Messaging của GĐ5 hiện chỉ có Application/Domain/Infrastructure) thì chưa có hợp
        // đồng để so — bỏ qua, như glob của codegen FE.
        var names = Directory.GetDirectories(modules)
            .Select(module => Path.Combine(module, "Presentation"))
            .Where(Directory.Exists)
            .SelectMany(presentation => Directory.GetFiles(presentation, "*-v1.yaml"))
            .Select(Path.GetFileName)
            .OfType<string>();

        var set = new SortedSet<string>(names, StringComparer.Ordinal);
        Assert.NotEmpty(set);   // glob không khớp gì = đổi chỗ thư mục; đừng để ca dưới xanh vì so hai tập rỗng
        return set;
    }

    /// <summary>Mọi lớp cụ thể kế thừa <see cref="ContractTestsBase"/> trong assembly test này.</summary>
    private static IReadOnlyList<Type> ContractTestClasses() =>
        [.. typeof(ContractTestsBase).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ContractTestsBase)))];

    /// <summary>
    /// Đọc <c>ContractFileName</c> mà không dựng <see cref="ApiFactory"/>: getter của lớp con trả hằng, không đụng trường nào,
    /// nên đối tượng chưa khởi tạo là đủ.
    /// </summary>
    private static string ContractFileNameOf(Type type)
    {
        var property = typeof(ContractTestsBase).GetProperty(
            "ContractFileName", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)property.GetValue(RuntimeHelpers.GetUninitializedObject(type))!;
    }

    [Fact]
    public void Moi_hop_dong_trong_src_deu_duoc_chep_vao_thu_muc_Contracts_cua_test()
    {
        var copied = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Contracts"))
            ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Contracts"), "*.yaml")
                .Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];

        var missing = SourceContracts().Where(name => !copied.Contains(name)).ToList();

        Assert.True(missing.Count == 0,
            "Hợp đồng chưa được chép vào output test — thêm <Content Include=... Link=\"Contracts\\<file>\"> vào "
          + "SocialApp.IntegrationTests.csproj: " + string.Join(", ", missing));
    }

    [Fact]
    public void Moi_hop_dong_co_dung_mot_lop_ContractTestsBase()
    {
        var byFile = ContractTestClasses()
            .GroupBy(ContractFileNameOf, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToList(), StringComparer.Ordinal);

        var source = SourceContracts();
        var uncovered = source.Where(name => !byFile.ContainsKey(name)).ToList();
        var duplicated = byFile.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}").ToList();
        var orphans = byFile.Keys.Where(name => !source.Contains(name)).ToList();

        Assert.True(uncovered.Count == 0,
            "Hợp đồng không có lớp so nào — thêm lớp kế thừa ContractTestsBase (chép SocialGraphContractTests): "
          + string.Join(", ", uncovered));
        Assert.True(duplicated.Count == 0, "Một hợp đồng có nhiều lớp so: " + string.Join(" | ", duplicated));
        Assert.True(orphans.Count == 0,
            "Lớp so trỏ tới hợp đồng không có trong src (đổi tên file mà quên sửa lớp?): " + string.Join(", ", orphans));
    }

    /// <summary>
    /// Điều bắt buộc số 1 của <see cref="ContractTestsBase"/>. Thiếu trait thì lớp đó vẫn chạy ở bước test chung của CI
    /// (bộ lọc <c>Category!=Contract</c> vẫn nhặt nó), nhưng bước "API contract (CI GATE)" thiếu nó mà vẫn xanh — cổng mang
    /// tên hợp đồng không còn nói đúng nó đã so những gì.
    /// </summary>
    [Fact]
    public void Moi_lop_ContractTestsBase_mang_trait_Category_Contract()
    {
        var missing = ContractTestClasses()
            .Where(type => !type.GetCustomAttributesData().Any(a =>
                a.AttributeType == typeof(TraitAttribute)
                && a.ConstructorArguments.Count == 2
                && Equals(a.ConstructorArguments[0].Value, "Category")
                && Equals(a.ConstructorArguments[1].Value, "Contract")))
            .Select(type => type.Name)
            .ToList();

        Assert.True(missing.Count == 0, "Thiếu [Trait(\"Category\", \"Contract\")]: " + string.Join(", ", missing));
    }
}
