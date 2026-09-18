// ArchUnitNET.Domain cung co kieu ten "Assembly" -> dat alias de khong nhap nhang.
using ArchUnitNET.Domain;
using ReflectionAssembly = System.Reflection.Assembly;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Ràng buộc kiến trúc ADR-001: chỉ tầng Infrastructure được chạm EF Core / Npgsql. Domain và
/// Application phải sạch persistence.
///
/// Vì sao cần lưới này: từ GĐ1, module Identity đã tham chiếu EF nên `DbSet`, `[Column]`,
/// `.Include()` trở thành có sẵn ở MỌI file trong module — compiler không phân biệt thư mục
/// Domain/ với Infrastructure/. Tiêm thẳng DbContext vào một Application service thì vẫn compile,
/// vẫn chạy đúng, và không ai bắt được trong code review. Giá phải trả đến muộn: không unit test
/// được logic nghiệp vụ nếu không dựng cả database, và GĐ2–GĐ6 sẽ copy đúng khuôn sai đó.
/// </summary>
public sealed class PersistenceBoundaryTests
{
    private static readonly string[] ModuleNames =
        ["Identity", "Profile", "SocialGraph", "Content", "Messaging", "Notification", "Moderation"];

    // Assembly EF/Npgsql PHẢI được nạp vào Architecture: NotDependOnAny so với tập type có thật
    // trong đồ thị. Không nạp thì vế phải rỗng và rule luôn xanh — lưới giả.
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(ModuleNames
            .Select(m => ReflectionAssembly.Load($"SocialApp.Modules.{m}"))
            .Append(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly)
            .Append(typeof(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder).Assembly)
            .ToArray())
        .Build();

    [Fact]
    public void Domain_and_Application_must_not_depend_on_EfCore()
    {
        foreach (var module in ModuleNames)
        {
            IArchRule rule = Types()
                .That().ResideInNamespace($"SocialApp.Modules.{module}.(Domain|Application)", useRegularExpressions: true)
                .Should().NotDependOnAny(
                    Types().That().ResideInNamespace("Microsoft.EntityFrameworkCore", useRegularExpressions: true)
                    .Or().ResideInNamespace("Npgsql", useRegularExpressions: true))
                .Because($"tầng Domain/Application của module {module} phải sạch persistence (ADR-001) — "
                       + "chỉ Infrastructure được chạm EF Core")
                .WithoutRequiringPositiveResults();

            rule.Check(Architecture);
        }
    }

    /// <summary>
    /// Canh gác chống "lưới giả": rule ở trên dùng WithoutRequiringPositiveResults nên nếu gõ sai
    /// namespace, nó KHÔNG khớp type nào và xanh vĩnh viễn. Test này bắt đúng chuyện đó.
    /// Đã gỡ Skip ở A1/A7 (GĐ1 khối A) khi Modules/Identity/Domain có entity đầu tiên — từ đây rule
    /// persistence chạy trên tập type có thật.
    /// </summary>
    [Fact]
    public void Identity_Domain_namespace_must_not_be_empty()
    {
        var types = Architecture.Types
            .Where(t => t.FullName.StartsWith("SocialApp.Modules.Identity.Domain", StringComparison.Ordinal))
            .ToList();

        Assert.True(types.Count > 0,
            "Không có type nào trong SocialApp.Modules.Identity.Domain — rule persistence boundary "
          + "đang chạy trong chân không. Kiểm tra lại namespace trong PersistenceBoundaryTests.");
    }

    /// <summary>
    /// Bản Profile của cùng cái canh gác trên (A7, GĐ2 khối A). Trước A1, namespace
    /// SocialApp.Modules.Profile.Domain rỗng nên rule persistence ở trên xanh vĩnh viễn trên module
    /// này — lưới không có dây. Test đi cùng commit của A1 để không thành nợ.
    /// </summary>
    [Fact]
    public void Profile_Domain_namespace_must_not_be_empty()
    {
        var types = Architecture.Types
            .Where(t => t.FullName.StartsWith("SocialApp.Modules.Profile.Domain", StringComparison.Ordinal))
            .ToList();

        Assert.True(types.Count > 0,
            "Không có type nào trong SocialApp.Modules.Profile.Domain — rule persistence boundary "
          + "đang chạy trong chân không. Kiểm tra lại namespace trong PersistenceBoundaryTests.");
    }

    /// <summary>
    /// Bản Content của cùng cái canh gác trên (A7, GĐ2 khối A). Đi cùng commit của A4 — entity Content đầu
    /// tiên — để rule persistence không còn chạy trong chân không trên module này.
    /// </summary>
    [Fact]
    public void Content_Domain_namespace_must_not_be_empty()
    {
        var types = Architecture.Types
            .Where(t => t.FullName.StartsWith("SocialApp.Modules.Content.Domain", StringComparison.Ordinal))
            .ToList();

        Assert.True(types.Count > 0,
            "Không có type nào trong SocialApp.Modules.Content.Domain — rule persistence boundary "
          + "đang chạy trong chân không. Kiểm tra lại namespace trong PersistenceBoundaryTests.");
    }
}
