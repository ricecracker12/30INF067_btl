using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SocialApp.SharedKernel.Authorization;
using Xunit;
using ReflectionAssembly = System.Reflection.Assembly;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Cổng 10.4 #6 của giai-doan-6.md (C4): mọi action thuộc nhóm Swagger <c>admin-v1</c>, <c>moderation-v1</c> — TRỪ đúng
/// <c>POST /reports</c> — mang <see cref="PrivilegedEndpointAttribute"/> (ở action hoặc ở controller). Quên một cái là một endpoint
/// quản trị fail-open khi Redis chết và KHÔNG ghi audit khi bị chặn — không lỗi, không log, không test chức năng nào thấy.
///
/// <c>POST /reports</c> là ngoại lệ DUY NHẤT và cố ý (B.10 tự rà #8): người dùng thường gửi báo cáo; fail-closed ở đó là chặn
/// người dùng vì Redis, audit ở đó là ngập bảng. Nhận diện bằng method + đường (<c>POST</c>, đường kết thúc bằng <c>reports</c>),
/// không bằng tên lớp — tên chưa tồn tại lúc viết test này.
///
/// Reflection trên assembly MODULE, không quét assembly test: probe controller của test không thuộc nhóm nào.
/// </summary>
public sealed class PrivilegedEndpointTests
{
    private static readonly string[] ModuleNames =
        ["Identity", "Profile", "SocialGraph", "Content", "Messaging", "Notification", "Moderation"];

    private static readonly string[] PrivilegedGroups = ["admin-v1", "moderation-v1"];

    private static List<(Type Controller, MethodInfo Action)> PrivilegedGroupActions() =>
        ModuleNames
            .Select(m => ReflectionAssembly.Load($"SocialApp.Modules.{m}"))
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => PrivilegedGroups.Contains(t.GetCustomAttribute<ApiExplorerSettingsAttribute>()?.GroupName))
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (t, m)))
            .ToList();

    private static bool IsCreateReport(Type controller, MethodInfo action)
    {
        var post = action.GetCustomAttributes<HttpPostAttribute>().FirstOrDefault();
        if (post is null)
            return false;

        var route = string.Join('/', new[] { controller.GetCustomAttribute<RouteAttribute>()?.Template, post.Template }
            .Where(s => !string.IsNullOrEmpty(s)))
            .TrimEnd('/');
        return route.EndsWith("reports", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Privileged_controllers_carry_the_attribute()
    {
        var missing = PrivilegedGroupActions()
            .Where(x => !IsCreateReport(x.Controller, x.Action))
            .Where(x => x.Action.GetCustomAttribute<PrivilegedEndpointAttribute>() is null
                     && x.Controller.GetCustomAttribute<PrivilegedEndpointAttribute>(inherit: true) is null)
            .Select(x => $"{x.Controller.FullName}.{x.Action.Name}")
            .ToList();

        Assert.True(missing.Count == 0,
            "Action thuộc admin-v1/moderation-v1 thiếu [PrivilegedEndpoint] — fail-open khi Redis chết và không audit khi bị chặn "
          + "(Đ-6.8, Đ-6.15): " + string.Join(", ", missing));
    }

    /// <summary>
    /// Canh gác chân không, cùng bài học <c>PersistenceBoundaryTests</c>: không có action nào trong hai nhóm thì test trên xanh
    /// vĩnh viễn. Ngày C4 (2026-09-23) CHƯA có controller admin/moderation nào — Skip có địa chỉ: gỡ ở D2 (controller
    /// <c>admin-v1</c> đầu tiên), và thử cho đỏ test trên ở đó (bỏ attribute khỏi một controller → đỏ nêu đúng tên).
    /// </summary>
    [Fact(Skip = "Gỡ ở D2 của GĐ6 — chưa có controller admin-v1/moderation-v1 nào (L-C7 hướng dẫn khối A+C)")]
    public void Privileged_groups_are_not_empty()
    {
        Assert.NotEmpty(PrivilegedGroupActions());
    }
}
