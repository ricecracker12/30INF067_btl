namespace SocialApp.Modules.Messaging.Domain;

/// <summary>
/// Cặp thành viên chuẩn hóa của một hội thoại (Đ-5.2): <c>(UserA, UserB)</c> với <c>UserA &lt; UserB</c> theo ĐÚNG thứ tự so
/// sánh uuid của Postgres — thứ tự mà <c>ck_conversations_order</c> kiểm. Chép quy tắc của <c>FriendPair</c> (SocialGraph,
/// <c>7e6e0d5</c>) vì Messaging không import được SocialGraph (<c>ModuleBoundaryTests</c>).
///
/// <c>Guid.CompareTo</c> so từng trường như chuỗi hex hiển thị — khớp Postgres. <c>ToByteArray()</c> thì KHÔNG khớp (ba nhóm
/// đầu little-endian): chuẩn hóa bằng mảng byte là sai chiều với khoảng một nửa số cặp, lộ ra dưới dạng <c>23514</c> lúc
/// INSERT — 500 chỉ với một số cặp người dùng.
///
/// Constructor <c>private</c>: <see cref="Of"/> là đường duy nhất tạo được một cặp.
/// </summary>
public readonly record struct ConversationPair
{
    private ConversationPair(Guid userA, Guid userB)
    {
        UserA = userA;
        UserB = userB;
    }

    public Guid UserA { get; }

    public Guid UserB { get; }

    public static ConversationPair Of(Guid a, Guid b)
    {
        if (a == b)
            throw new ArgumentException("Không có hội thoại nào của một người với chính mình.", nameof(b));

        return a.CompareTo(b) < 0 ? new(a, b) : new(b, a);
    }
}
