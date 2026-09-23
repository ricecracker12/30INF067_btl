using ReflectionAssembly = System.Reflection.Assembly;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Identity.Domain;
using SocialApp.SharedKernel.Authorization;
using Xunit;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Lưới chống gõ sai mã quyền (C1). <c>[RequirePermission("post.hid")]</c> compile được, và Admin VẪN QUA —
/// short-circuit không nhìn mã quyền. Người test tay bằng tài khoản Admin thấy "chạy tốt", còn Moderator bị
/// chặn vĩnh viễn. Test này so mọi mã trong attribute với tập hằng số của <see cref="PermissionCodes"/>.
///
/// Ở GĐ1 chưa module nào dùng attribute nên rule chạy trong chân không — cùng bài học với
/// <see cref="PersistenceBoundaryTests"/>. Đã thử một lần cho đỏ: thêm tạm <c>[RequirePermission("post.hid")]</c>
/// vào một action của module Identity → đỏ đúng mã → gỡ.
///
/// Dùng reflection thay ArchUnitNET vì phải đọc GIÁ TRỊ của attribute, không chỉ sự tồn tại.
/// </summary>
public sealed class PermissionCodeUsageTests
{
    private static readonly string[] ModuleNames =
        ["Identity", "Profile", "SocialGraph", "Content", "Messaging", "Notification", "Moderation"];

    private static HashSet<string> KnownCodes() => typeof(PermissionCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void RequirePermission_chi_dung_ma_quyen_co_trong_PermissionCodes()
    {
        var known = KnownCodes();

        var controllers = ModuleNames
            .Select(m => ReflectionAssembly.Load($"SocialApp.Modules.{m}"))
            .Append(typeof(Program).Assembly)   // SocialApp.Api — controller hạ tầng của host
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        // GĐ6 C4: đọc cả [RequireAnyPermission] (Mục 10.4 #4) — mỗi mã trong danh sách của nó được kiểm như một [RequirePermission].
        static IEnumerable<string> Codes(MemberInfo member, bool inherit) =>
            member.GetCustomAttributes<RequirePermissionAttribute>(inherit).Select(a => a.Permission)
                .Concat(member.GetCustomAttributes<RequireAnyPermissionAttribute>(inherit).SelectMany(a => a.Permissions));

        var unknown = controllers
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => (Where: $"{t.FullName}.{m.Name}", Codes: Codes(m, inherit: false)))
                .Prepend((Where: t.FullName!, Codes: Codes(t, inherit: true))))
            .SelectMany(x => x.Codes.Select(code => (x.Where, Permission: code)))
            .Where(x => !known.Contains(x.Permission))
            .Select(x => $"{x.Where}: \"{x.Permission}\"")
            .ToList();

        Assert.True(unknown.Count == 0,
            "[RequirePermission] dùng mã quyền không có trong PermissionCodes (gõ sai? Admin vẫn qua nên test "
          + "tay bằng Admin không thấy): " + string.Join(", ", unknown));
    }

    /// <summary>
    /// Canh gác vế bên kia: đọc hằng số sai cách (vd đổi sang static readonly) thì tập mã rỗng và rule trên
    /// đỏ với MỌI attribute — hoặc tệ hơn, ai đó "sửa" bằng cách nới rule. Khóa đúng 18 mã: 17 của Mục 5.2 (GĐ1) + <c>role.manage</c>
    /// (GĐ6 Đ-6.9). Thêm mã mới thì sửa số này có chủ đích, trong cùng commit với migration/seeder.
    /// </summary>
    [Fact]
    public void PermissionCodes_doc_duoc_du_18_ma()
    {
        Assert.Equal(18, KnownCodes().Count);
    }
}
