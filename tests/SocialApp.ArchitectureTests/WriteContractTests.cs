using System.Data.Common;
using System.Reflection;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Moderation;
using Xunit;
using ReflectionAssembly = System.Reflection.Assembly;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Cổng 10.4 #5 của giai-doan-6.md (Đ-6.3, C2): dự án có ĐÚNG HAI hợp đồng ghi xuyên module — <see cref="IAuditTrail"/> và
/// <see cref="IModerationTargets"/> — và dấu hiệu nhận ra hợp đồng ghi là phương thức nhận <see cref="DbTransaction"/> của người gọi.
///
/// Luật hai vế (vế nào thiếu cũng sai, L-C9 / cạm bẫy 2 Mục 12 của hướng dẫn khối A+C):
/// <list type="number">
/// <item>Mọi INTERFACE có phương thức nhận <see cref="DbTransaction"/> nằm trong <c>SharedKernel.Audit</c> hoặc
/// <c>SharedKernel.Moderation</c>. Một interface như vậy ở module khác là hợp đồng ghi thứ ba mở lén.</item>
/// <item>Mọi LỚP (module hay SharedKernel) có phương thức nhận <see cref="DbTransaction"/> phải hiện thực một interface ở vế 1 — tức là
/// đang hiện thực hợp đồng, không phải một đường ghi tự chế. Viết ngây thơ "chỉ hai namespace được có phương thức như vậy" thì đỏ
/// ngay với hai provider hợp lệ ở Content, Profile.</item>
/// </list>
/// Muốn thêm hợp đồng ghi thứ ba: đó là một quyết định mới có ngày ở giai-doan-6.md, và sửa danh sách namespace dưới đây cùng commit.
/// </summary>
public sealed class WriteContractTests
{
    private static readonly string[] AllowedNamespaces = ["SocialApp.SharedKernel.Audit", "SocialApp.SharedKernel.Moderation"];

    private static readonly string[] ModuleNames =
        ["Identity", "Profile", "SocialGraph", "Content", "Messaging", "Notification", "Moderation"];

    private static IEnumerable<Type> AllTypes() => ModuleNames
        .Select(m => ReflectionAssembly.Load($"SocialApp.Modules.{m}"))
        .Append(typeof(IAuditTrail).Assembly)   // SharedKernel
        .SelectMany(a => a.GetTypes())
        .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false));

    private static bool TakesTransaction(Type type) => type
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Any(m => m.GetParameters().Any(p => typeof(DbTransaction).IsAssignableFrom(p.ParameterType)));

    [Fact]
    public void WriteContracts_are_only_the_two_named()
    {
        var types = AllTypes().ToList();

        var contracts = types.Where(t => t.IsInterface && TakesTransaction(t)).ToList();
        var strayContracts = contracts
            .Where(t => !AllowedNamespaces.Contains(t.Namespace))
            .Select(t => t.FullName)
            .ToList();

        var implementers = types.Where(t => t is { IsClass: true } && TakesTransaction(t)).ToList();
        var strayImplementers = implementers
            .Where(t => !t.GetInterfaces().Any(i => AllowedNamespaces.Contains(i.Namespace) && TakesTransaction(i)))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(strayContracts.Count == 0,
            "Interface nhận DbTransaction ngoài SharedKernel.Audit/SharedKernel.Moderation — hợp đồng ghi thứ ba cần một quyết định "
          + "mới (Đ-6.3): " + string.Join(", ", strayContracts));
        Assert.True(strayImplementers.Count == 0,
            "Lớp có phương thức nhận DbTransaction mà không hiện thực hợp đồng ghi nào — đường ghi xuyên module tự chế (Đ-6.3): "
          + string.Join(", ", strayImplementers));
    }

    /// <summary>
    /// Canh gác chân không: không tìm thấy hợp đồng ghi nào (đổi tên namespace, đổi kiểu tham số sang <c>IDbTransaction</c>…) thì
    /// test trên xanh vĩnh viễn. Khóa đúng ba interface hiện có, và ít nhất hai lớp hiện thực ở module.
    /// </summary>
    [Fact]
    public void WriteContracts_are_found()
    {
        var types = AllTypes().ToList();

        Assert.Equal(
            [typeof(IAuditTrail).FullName, typeof(IModerationTargetProvider).FullName, typeof(IModerationTargets).FullName],
            types.Where(t => t.IsInterface && TakesTransaction(t)).Select(t => t.FullName).Order());

        Assert.True(types.Count(t => t.IsClass && TakesTransaction(t) && t.Assembly != typeof(IAuditTrail).Assembly) >= 3,
            "Thiếu hiện thực hợp đồng ghi ở module (SqlAuditTrail, ContentModerationTargets, ProfileModerationTargets)?");
    }
}
