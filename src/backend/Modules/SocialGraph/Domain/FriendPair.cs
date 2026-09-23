namespace SocialApp.Modules.SocialGraph.Domain;

/// <summary>
/// Cặp chuẩn hóa của BR-03: (Min, Max) với Min &lt; Max theo ĐÚNG thứ tự so sánh uuid của Postgres.
/// Guid.CompareTo so từng trường như chuỗi hex hiển thị — khớp Postgres. ToByteArray() thì KHÔNG khớp
/// (ba nhóm đầu little-endian): so mảng byte là chuẩn hóa sai chiều với khoảng một nửa số cặp.
///
/// Constructor <c>private</c>, KHÔNG dạng positional: <c>new FriendPair(a, b)</c> sẽ đi vòng qua <see cref="Of"/> và
/// sai chiều với khoảng một nửa số cặp — lỗi chỉ lộ ra dưới dạng <c>23514</c> lúc INSERT. <see cref="Of"/> là đường
/// duy nhất tạo được một cặp. (<c>default(FriendPair)</c> vẫn tồn tại vì là struct — đừng dùng nó.)
/// </summary>
public readonly record struct FriendPair
{
    private FriendPair(Guid min, Guid max)
    {
        Min = min;
        Max = max;
    }

    public Guid Min { get; }

    public Guid Max { get; }

    public static FriendPair Of(Guid a, Guid b)
    {
        if (a == b)
            throw new ArgumentException("Không có cặp quan hệ nào của một người với chính mình.", nameof(b));

        return a.CompareTo(b) < 0 ? new(a, b) : new(b, a);
    }

    public bool Contains(Guid userId) => userId == Min || userId == Max;

    public Guid Other(Guid userId)
    {
        if (userId == Min)
            return Max;
        if (userId == Max)
            return Min;

        throw new ArgumentException("userId không thuộc cặp quan hệ này.", nameof(userId));
    }
}
