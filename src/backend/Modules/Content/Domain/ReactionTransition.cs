namespace SocialApp.Modules.Content.Domain;

/// <summary>Việc phải làm trên dòng <c>reactions</c> của người gọi (bảng bốn nhánh, Mục 7.4 GĐ3).</summary>
public enum ReactionWrite
{
    /// <summary>Không đổi gì — trạng thái mong muốn đã là trạng thái hiện tại (idempotent, Đ-3.7).</summary>
    None,
    Insert,
    Update,
    Delete,
}

/// <summary>
/// Kết quả của <see cref="ReactionTransition.Apply"/>: một thao tác trên dòng <c>reactions</c> cộng tối đa HAI thay đổi bộ đếm —
/// loại cũ −1 (<see cref="Decrement"/>), loại mới +1 (<see cref="Increment"/>). Hai giá trị không bao giờ bằng nhau.
/// </summary>
public readonly record struct ReactionChange(ReactionWrite Write, ReactionType? Decrement, ReactionType? Increment);

/// <summary>
/// C1 GĐ3 — bảng bốn nhánh của Mục 7.4 dưới dạng HÀM THUẦN. Transaction Đ-3.8 (C2) đọc cảm xúc hiện tại SAU khi khóa dòng đối
/// tượng rồi hỏi hàm này phải làm gì — nhờ vậy luật "đổi loại = −1 cũ, +1 mới; giống nhau = không làm gì" có unit test mà không
/// cần Postgres, và store không có <c>if</c> nghiệp vụ nào.
/// </summary>
public static class ReactionTransition
{
    /// <param name="current">Cảm xúc hiện có của người gọi trên đối tượng, <c>null</c> nếu chưa thả.</param>
    /// <param name="desired">Cảm xúc muốn có sau request: <c>PUT</c> → loại gửi lên; <c>DELETE</c> → <c>null</c>.</param>
    public static ReactionChange Apply(ReactionType? current, ReactionType? desired) =>
        (current, desired) switch
        {
            (null, null) => new(ReactionWrite.None, null, null),
            (null, { } add) => new(ReactionWrite.Insert, null, add),
            ({ } remove, null) => new(ReactionWrite.Delete, remove, null),
            ({ } from, { } to) when from == to => new(ReactionWrite.None, null, null),
            ({ } from, { } to) => new(ReactionWrite.Update, from, to),
        };
}
