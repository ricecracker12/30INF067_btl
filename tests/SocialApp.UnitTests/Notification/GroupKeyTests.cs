using System.Reflection;
using SocialApp.Modules.Notification.Domain;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Moderation;
using Xunit;

namespace SocialApp.UnitTests.Notification;

/// <summary>
/// Canh <see cref="GroupKey"/> và <see cref="NotificationTypes"/> (A2 GĐ6, Đ-6.16, Đ-6.17). Khóa gộp sai không làm gì đỏ cả:
/// hai chỗ dựng khác nhau thì mỗi sự kiện thành một thông báo riêng; hai loại trùng khóa thì thông báo của loại này ghi đè
/// loại kia. Cả hai chỉ lộ ra khi người dùng thật đếm chuông.
/// </summary>
public sealed class GroupKeyTests
{
    private static readonly Guid A = Guid.Parse("01900000-0000-7000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("01900000-0000-7000-8000-00000000000b");

    /// <summary>Tám loại, mỗi loại một khóa mẫu — nguyên văn cột <c>group_key</c> của bảng Đ-6.17.</summary>
    public static TheoryData<string, string> TamLoai => new()
    {
        { GroupKey.Comment(A), "comment:post:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Reply(A), "reply:comment:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Reaction(ReactionTargetKind.Post, A), "reaction:post:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Reaction(ReactionTargetKind.Comment, A), "reaction:comment:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.FriendRequest(A), "friend_request:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.FriendAccepted(A), "friend_accepted:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Message(A), "message:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Moderation(ModerationTargetType.Post, A), "moderation:post:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Moderation(ModerationTargetType.Comment, A), "moderation:comment:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Moderation(ModerationTargetType.User, A), "moderation:user:01900000-0000-7000-8000-00000000000a" },
        { GroupKey.Tag(A), "tag:comment:01900000-0000-7000-8000-00000000000a" },
    };

    [Theory]
    [MemberData(nameof(TamLoai))]
    public void Moi_loai_ra_dung_chuoi_mau(string actual, string expected) => Assert.Equal(expected, actual);

    /// <summary>
    /// Mọi khóa của mọi loại, với mọi giá trị enum — cho các ca đọc "cả tập". Dựng bằng <see cref="Enum.GetValues{TEnum}"/> để
    /// thêm thành viên enum thì ca dài nhất/khác nhau tự phủ nó.
    /// </summary>
    private static List<(string Type, string Key)> AllKeys(Guid id) =>
    [
        (NotificationTypes.Comment, GroupKey.Comment(id)),
        (NotificationTypes.Reply, GroupKey.Reply(id)),
        .. Enum.GetValues<ReactionTargetKind>().Select(k => (NotificationTypes.Reaction, GroupKey.Reaction(k, id))),
        (NotificationTypes.FriendRequest, GroupKey.FriendRequest(id)),
        (NotificationTypes.FriendAccepted, GroupKey.FriendAccepted(id)),
        (NotificationTypes.Message, GroupKey.Message(id)),
        .. Enum.GetValues<ModerationTargetType>().Select(t => (NotificationTypes.Moderation, GroupKey.Moderation(t, id))),
        (NotificationTypes.Tag, GroupKey.Tag(id)),
    ];

    /// <summary>Tám loại đều có hàm dựng khóa — thêm loại vào <see cref="NotificationTypes.All"/> mà quên hàm thì đỏ.</summary>
    [Fact]
    public void Tam_loai_deu_co_ham_dung_khoa()
    {
        Assert.Equal(8, NotificationTypes.All.Length);   // bảng Đ-6.17 — thêm loại mới thì sửa số này có chủ đích
        Assert.Equal(
            NotificationTypes.All.Order(StringComparer.Ordinal),
            AllKeys(A).Select(k => k.Type).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>Tiền tố của khóa là đúng loại của nó — đọc một dòng là biết nó thuộc loại nào, và hai loại không đụng khóa.</summary>
    [Fact]
    public void Tien_to_khoa_la_loai_cua_no()
    {
        Assert.All(AllKeys(A), k => Assert.StartsWith(k.Type + ":", k.Key, StringComparison.Ordinal));
    }

    /// <summary>Hai id khác nhau ra hai khóa khác nhau; và không hai hàm nào (kể cả hai giá trị enum) ra cùng một khóa.</summary>
    [Fact]
    public void Khoa_khac_nhau_theo_id_va_theo_loai()
    {
        var keysA = AllKeys(A).Select(k => k.Key).ToList();
        var keysB = AllKeys(B).Select(k => k.Key).ToList();

        Assert.Equal(keysA.Count, keysA.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(keysA.Intersect(keysB, StringComparer.Ordinal));
        Assert.NotEqual(GroupKey.Reaction(ReactionTargetKind.Post, A), GroupKey.Reaction(ReactionTargetKind.Comment, A));
    }

    /// <summary>Cột <c>group_key</c> là <c>varchar(120)</c> — khóa dài hơn là lỗi 22001 ngay trong handler thông báo.</summary>
    [Fact]
    public void Khoa_dai_nhat_vua_cot_group_key()
    {
        var longest = AllKeys(Guid.NewGuid()).Max(k => k.Key.Length);

        Assert.True(longest <= GroupKey.MaxLength, $"Khóa dài nhất {longest} ký tự > {GroupKey.MaxLength}.");
    }

    /// <summary>
    /// Định dạng <c>:D</c> cố định, không phụ thuộc cách <c>Guid</c> được tạo: <c>Guid.Parse</c> chữ HOA vẫn ra khóa chữ
    /// thường — nếu không, cùng một bài đến từ hai nguồn (route vs event) thành hai nhóm.
    /// </summary>
    [Fact]
    public void Guid_chu_hoa_hay_thuong_deu_ra_cung_khoa()
    {
        var upper = Guid.Parse("01900000-0000-7000-8000-00000000000A");

        Assert.Equal(GroupKey.Comment(A), GroupKey.Comment(upper));
    }

    /// <summary>Hai cột <c>type</c>, <c>target_type</c> là <c>varchar(20)</c>; và không hằng nào trùng chuỗi.</summary>
    [Fact]
    public void Hang_loai_va_dich_vua_cot_va_khong_trung()
    {
        Assert.All(NotificationTypes.All, t => Assert.InRange(t.Length, 1, NotificationTypes.MaxLength));
        Assert.All(NotificationTargetTypes.All, t => Assert.InRange(t.Length, 1, NotificationTargetTypes.MaxLength));
        Assert.Equal(NotificationTypes.All.Length, NotificationTypes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(NotificationTargetTypes.All.Length, NotificationTargetTypes.All.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Thêm hằng mà quên thêm vào <c>All</c> thì CHECK của DB chặn đúng loại đó — đỏ ở đây trước.</summary>
    [Theory]
    [InlineData(typeof(NotificationTypes))]
    [InlineData(typeof(NotificationTargetTypes))]
    public void All_liet_ke_du_moi_hang(Type type)
    {
        var constants = type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        var all = (string[])type.GetField("All")!.GetValue(null)!;

        Assert.Equal(constants.Count, all.Length);
        Assert.Empty(constants.Except(all, StringComparer.Ordinal));
    }

    /// <summary>Giá trị enum lạ (thêm thành viên mà quên nhánh) → ném, không sinh một khóa lệch.</summary>
    [Fact]
    public void Enum_la_thi_nem()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupKey.Reaction((ReactionTargetKind)99, A));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupKey.Moderation((ModerationTargetType)99, A));
    }
}
