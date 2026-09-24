using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Content.Domain;
using SocialApp.Modules.Content.Infrastructure.Configurations;

namespace SocialApp.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// Những thứ SQL của GĐ3 cần biết về một đối tượng nhận cảm xúc: tên bảng, cột khóa, điều kiện "còn sống". Tên bảng/cột là HẰNG
/// trong code — không bao giờ ghép từ input (C2) — nên ghép chuỗi SQL ở đây an toàn; mọi GIÁ TRỊ đi bằng tham số.
/// </summary>
internal sealed record InteractionTarget(string Table, string Key, string AliveSql)
{
    private static readonly InteractionTarget Post = new(
        "content.posts", "post_id", LowercaseEnum.EqualsSql("status", PostStatus.Published));

    private static readonly InteractionTarget Comment = new(
        "content.comments", "comment_id", LowercaseEnum.EqualsSql("status", CommentStatus.Visible));

    public static InteractionTarget Of(ReactionTargetType type) => type switch
    {
        ReactionTargetType.Post => Post,
        ReactionTargetType.Comment => Comment,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Bước 1 của Đ-3.8: khóa dòng đối tượng, chỉ khi còn sống. 0 dòng → người gọi trả 404.</summary>
    public string LockSql => $"select {Key} as \"Value\" from {Table} where {Key} = @id and {AliveSql} for update";
}

/// <summary>
/// C2 GĐ3 — câu SQL bộ đếm cảm xúc viết MỘT lần cho cả <c>posts</c> và <c>comments</c> (Đ-3.8 bước 4). Một câu <c>UPDATE</c> với
/// biểu thức trên <c>jsonb</c>, không nạp dictionary lên rồi ghi xuống: khóa ở bước 1 đã tuần tự hóa, nhưng viết nguyên tử thì bộ
/// đếm vẫn đúng kể cả khi ai đó sau này gỡ khóa "cho nhanh". Loại về 0 thì XÓA KHÓA — <c>{}</c> vẫn là <c>{}</c>, không bao giờ
/// <c>{"like":0}</c> (REACT-05). Không chạm <c>updated_at</c>/<c>edited_at</c>: bộ đếm là thống kê, không phải nội dung (REACT-07).
/// </summary>
internal static class ReactionCountsSql
{
    /// <summary>
    /// <c>UPDATE … SET reaction_counts = …  RETURNING reaction_counts::text</c> với tham số <c>@id</c>, <c>@dec</c> (khi
    /// <paramref name="decrement"/>), <c>@inc</c> (khi <paramref name="increment"/>). Hai khóa không bao giờ trùng nhau
    /// (<see cref="ReactionTransition"/>), nên vế +1 đọc thẳng giá trị cũ của cột.
    /// </summary>
    public static string Update(InteractionTarget target, bool decrement, bool increment)
    {
        var expr = "reaction_counts";
        if (decrement)
            expr = "(case when coalesce((reaction_counts ->> @dec)::int, 0) <= 1 then reaction_counts - @dec "
                 + "else jsonb_set(reaction_counts, array[@dec], to_jsonb((reaction_counts ->> @dec)::int - 1)) end)";
        if (increment)
            expr = $"jsonb_set({expr}, array[@inc], to_jsonb(coalesce((reaction_counts ->> @inc)::int, 0) + 1))";

        return $"update {target.Table} set reaction_counts = {expr} where {target.Key} = @id "
             + "returning reaction_counts::text as \"Value\"";
    }

    public static string Read(InteractionTarget target) =>
        $"select reaction_counts::text as \"Value\" from {target.Table} where {target.Key} = @id";

    public static NpgsqlParameter Key(string name, ReactionType type) =>
        new(name, NpgsqlDbType.Text) { Value = LowercaseEnum.Name(type) };
}
