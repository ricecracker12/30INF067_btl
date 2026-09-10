using ArchUnitNET.Domain;
using ReflectionAssembly = System.Reflection.Assembly;
using System.Reflection;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Ràng buộc kiến trúc: module SỞ HỮU tầng HTTP của mình, nhưng chỉ tầng <c>Presentation</c> được
/// chạm ASP.NET Core MVC. <c>Domain</c>, <c>Application</c>, <c>Infrastructure</c> phải sạch HTTP.
///
/// Đây là bản song sinh của <see cref="PersistenceBoundaryTests"/> (chỉ Infrastructure chạm EF), và
/// lỗ hổng nó bịt đã tồn tại TỪ TRƯỚC khi module có controller: ASP.NET Core lọt vào mọi module qua
/// <c>SharedKernel</c> (FrameworkReference) và <c>FluentValidation.AspNetCore</c>. Nghĩa là hôm nay
/// viết <c>return NotFound();</c> trong một domain service vẫn compile, vẫn chạy đúng, và không ai
/// bắt được trong code review. Giá phải trả đến muộn: logic nghiệp vụ dính chặt vào HTTP, không
/// unit test được nếu không dựng cả pipeline — và GĐ2–GĐ6 sẽ copy đúng khuôn sai đó.
/// </summary>
public sealed class PresentationBoundaryTests
{
    private static readonly string[] ModuleNames =
        ["Identity", "Profile", "SocialGraph", "Content", "Messaging", "Notification", "Moderation"];

    /// <summary>
    /// Các tầng KHÔNG được chạm MVC. <c>DependencyInjection</c> nằm trong danh sách vì nó là bề mặt
    /// ráp DI của module cho host — cần MVC ở đó nghĩa là đang nhét việc của Presentation vào chỗ sai.
    /// </summary>
    private const string ForbiddenLayers = "(Domain|Application|Infrastructure|DependencyInjection)";

    // Assembly MVC PHẢI được nạp vào Architecture: NotDependOnAny so với tập type có thật trong đồ
    // thị. Không nạp thì vế phải rỗng và rule luôn xanh — lưới giả. Cùng bài học với
    // PersistenceBoundaryTests, nên có test canh gác bên dưới.
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(ModuleNames
            .Select(m => ReflectionAssembly.Load($"SocialApp.Modules.{m}"))
            .Append(typeof(ControllerBase).Assembly)          // Microsoft.AspNetCore.Mvc.Core
            .Append(typeof(IActionResult).Assembly)           // Microsoft.AspNetCore.Mvc.Abstractions
            .ToArray())
        .Build();

    [Fact]
    public void Inner_layers_must_not_depend_on_AspNetCore_Mvc()
    {
        foreach (var module in ModuleNames)
        {
            IArchRule rule = Types()
                .That().ResideInNamespace($"SocialApp.Modules.{module}.{ForbiddenLayers}", useRegularExpressions: true)
                .Should().NotDependOnAny(
                    Types().That().ResideInNamespace("Microsoft.AspNetCore.Mvc", useRegularExpressions: true))
                .Because($"chỉ tầng Presentation của module {module} được chạm HTTP; Domain/Application/"
                       + "Infrastructure phải sạch MVC để unit test được mà không dựng pipeline")
                .WithoutRequiringPositiveResults();

            rule.Check(Architecture);
        }
    }

    [Fact]
    public void Modules_must_not_depend_on_the_Api_host()
    {
        foreach (var module in ModuleNames)
        {
            IArchRule rule = Types()
                .That().ResideInNamespace($"SocialApp.Modules.{module}", useRegularExpressions: true)
                .Should().NotDependOnAny(
                    Types().That().ResideInNamespace("SocialApp.Api", useRegularExpressions: true))
                .Because($"module {module} phải đứng độc lập với host: host nạp module qua "
                       + "AddApplicationPart, không phải chiều ngược lại")
                .WithoutRequiringPositiveResults();

            rule.Check(Architecture);
        }
    }

    /// <summary>
    /// Canh gác chống "lưới giả": hai rule trên dùng WithoutRequiringPositiveResults nên nếu gõ sai
    /// namespace hoặc quên nạp assembly MVC, chúng KHÔNG khớp type nào và xanh vĩnh viễn.
    /// </summary>
    [Fact]
    public void Mvc_namespace_must_be_present_in_the_architecture()
    {
        var mvcTypes = Architecture.Types
            .Count(t => t.FullName.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal));

        Assert.True(mvcTypes > 0,
            "Không có type Microsoft.AspNetCore.Mvc nào trong Architecture — rule presentation "
          + "boundary đang chạy trong chân không. Kiểm tra lại LoadAssemblies.");
    }

    /// <summary>
    /// Program.cs lọc Swagger bằng <c>DocInclusionPredicate((doc, api) =&gt; api.GroupName == doc)</c>.
    /// Controller không khai <c>[ApiExplorerSettings(GroupName = ...)]</c> sẽ rơi khỏi MỌI trang
    /// Swagger — không exception, không log, endpoint chỉ đơn giản biến mất khỏi tài liệu, và cả
    /// IdentityContractTests lẫn lane frontend đều không thấy nó. Đúng loại hỏng câm mà chỉ có test
    /// mới bắt được.
    ///
    /// Dùng reflection thay ArchUnitNET vì ở đây phải đọc GIÁ TRỊ của thuộc tính, không chỉ sự tồn tại.
    /// </summary>
    [Fact]
    public void Every_controller_must_declare_a_swagger_group()
    {
        var assemblies = ModuleNames
            .Select(m => ReflectionAssembly.Load($"SocialApp.Modules.{m}"))
            .Append(typeof(Program).Assembly)   // SocialApp.Api — controller hạ tầng của host
            .ToArray();

        var controllers = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t is { IsAbstract: false, IsPublic: true })
            .ToList();

        var missing = controllers
            .Where(t => string.IsNullOrWhiteSpace(
                t.GetCustomAttribute<ApiExplorerSettingsAttribute>()?.GroupName))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(missing.Count == 0,
            "Controller thiếu [ApiExplorerSettings(GroupName = ...)] nên sẽ biến mất khỏi Swagger "
          + "mà không báo lỗi: " + string.Join(", ", missing));
    }
}
