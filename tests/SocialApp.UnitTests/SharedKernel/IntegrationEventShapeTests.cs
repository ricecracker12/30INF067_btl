using System.Reflection;
using SocialApp.SharedKernel.Events;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// EVT-07 — luật 3 của Đ-6.2 thành cổng CI: event chỉ mang id, enum, số, cờ; không nội dung, không tên hiển thị (Đ-5.18).
/// Event nằm trong hàng đợi bộ nhớ và có thể lọt vào log lỗi. A (GĐ3) và B (GĐ5) sẽ thêm record mà GĐ6 không review — test này
/// canh thay người review.
///
/// Muốn thêm một kiểu vào <see cref="AllowedTypes"/> hay một ngoại lệ vào <see cref="Exceptions"/> thì đó là quyết định mới, ghi
/// vào <c>giai-doan-6.md</c> Đ-6.2 — không sửa lặng ở đây.
/// </summary>
public sealed class IntegrationEventShapeTests
{
    private static readonly HashSet<Type> AllowedTypes =
    [
        typeof(Guid), typeof(Guid?), typeof(long), typeof(int), typeof(bool), typeof(DateTimeOffset),
        typeof(IReadOnlyList<Guid>),
    ];

    /// <summary>
    /// Ngoại lệ DUY NHẤT: mã lý do trong tập cố định của CHECK <c>reason_code</c> (Đ-6.12), không phải chữ người dùng gõ.
    /// </summary>
    private static readonly HashSet<(Type, string)> Exceptions =
    [
        (typeof(ContentHidden), nameof(ContentHidden.ReasonCode)),
    ];

    private static IReadOnlyList<Type> EventTypes() =>
        typeof(IIntegrationEvent).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EVT_07_Moi_event_la_sealed_va_chi_mang_id_enum_so_co()
    {
        var types = EventTypes();
        Assert.NotEmpty(types);   // không có event nào thì test xanh trong chân không

        var violations = new List<string>();
        foreach (var type in types)
        {
            if (!type.IsSealed)
                violations.Add($"{type.Name}: phải là sealed record (bus dispatch theo kiểu chính xác)");

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == "EqualityContract")
                    continue;   // thuộc tính do compiler sinh cho record

                var t = property.PropertyType;
                var allowed = AllowedTypes.Contains(t) || t.IsEnum || Nullable.GetUnderlyingType(t)?.IsEnum == true
                    || Exceptions.Contains((type, property.Name));
                if (!allowed)
                    violations.Add($"{type.Name}.{property.Name}: {t.Name}");
            }
        }

        Assert.True(violations.Count == 0,
            "Event chỉ được mang id, enum, số, cờ (Đ-6.2 luật 3):\n" + string.Join('\n', violations));
    }

    /// <summary>
    /// Sáu record của Đ-6.17 là chữ ký đã chốt với A và B — đổi tên hay xóa một cái là code của người khác trên <c>develop</c>
    /// hết compile. Thêm record mới thì không làm test này đỏ.
    /// </summary>
    [Fact]
    public void Sau_record_cua_D6_17_deu_co_mat()
    {
        var names = EventTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(names,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "CommentCreated", "ReactionSet", "FriendRequestSent", "FriendRequestAccepted", "MessageSent", "ContentHidden",
            });
    }
}
