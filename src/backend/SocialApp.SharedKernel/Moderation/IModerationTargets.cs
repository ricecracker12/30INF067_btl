using System.Data.Common;

namespace SocialApp.SharedKernel.Moderation;

/// <summary>Một đối tượng có thể bị báo cáo / kiểm duyệt: bài, bình luận, hoặc người dùng (Đ-6.12).</summary>
public readonly record struct ModerationTarget(ModerationTargetType Type, Guid Id);

/// <summary>
/// Ảnh chụp đối tượng cho Moderator (<c>GET /reports/{id}</c>, D7) và cho luật "không tự báo cáo mình" (D6).
///
/// Mang <see cref="AuthorId"/> và <see cref="MediaKeys"/>, KHÔNG mang tên hay URL: hydrate tên (<c>IUserDirectory</c>) và ký URL
/// (<c>IObjectStorage</c>) là việc của D7, một lô cho cả trang — cùng luật "SharedKernel không biết R2" của <c>UserCard</c>.
/// <see cref="Body"/> có mặt cả khi bài riêng tư hay đã xóa: Moderator phải thấy nội dung mới quyết được (Mục 8.1).
/// </summary>
/// <param name="Status">
/// Bài/bình luận: <c>published</c> · <c>hidden</c> · <c>deleted</c> (bình luận: <c>visible</c> · <c>hidden</c> · <c>deleted</c>).
/// Người dùng: <c>active</c> · <c>disabled</c>.
/// </param>
/// <param name="AuthorId">Tác giả bài/bình luận; với người dùng là chính người đó.</param>
/// <param name="PostId">Bài chứa đối tượng — với bài là chính nó, với bình luận là bài cha; người dùng thì null.</param>
public sealed record TargetSnapshot(
    ModerationTarget Target,
    string Status,
    Guid AuthorId,
    string? Body,
    IReadOnlyList<string> MediaKeys,
    Guid? PostId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt);

/// <summary>Kết quả ẩn — hợp đồng không biết HTTP; người gọi (D7) quyết định 200 hay 409 (Đ-6.3).</summary>
public enum HideOutcome
{
    /// <summary>Vừa chuyển <c>published → hidden</c>.</summary>
    Hidden,

    /// <summary>Đã ẩn từ trước (báo cáo thứ hai cho bài đã ẩn) — D7 vẫn đi tiếp, đóng báo cáo.</summary>
    AlreadyHidden,

    /// <summary>Không tồn tại hoặc đã xóa — D7 trả 409 <c>moderation-target-gone</c>.</summary>
    NotFound,
}

/// <summary>Kết quả khôi phục.</summary>
public enum RestoreOutcome
{
    /// <summary>Vừa chuyển <c>hidden → published</c>.</summary>
    Restored,

    /// <summary>Đang không ẩn — D7 trả 409 <c>moderation-not-hidden</c>.</summary>
    NotHidden,

    NotFound,
}

/// <summary>
/// Kiểm duyệt ghi hộ vào bảng của module chủ, TRONG transaction của Moderation — hợp đồng GHI thứ hai và cuối cùng của dự án.
///
/// <b>Lệch Đ-2.3 luật 1 có chủ đích</b> (Đ-6.3 của giai-doan-6.md): PTTK đòi "ẩn bài + đóng báo cáo + audit" trong MỘT transaction
/// (ENT-12, UC-19) mà bài thuộc Content, báo cáo thuộc Moderation. Truyền <see cref="DbTransaction"/> của Moderation vào đây: một
/// transaction Postgres thật, và Content vẫn là module DUY NHẤT viết SQL vào <c>content.posts</c>. Thêm hợp đồng ghi thứ ba là
/// một quyết định mới, có ngày — <c>WriteContracts_are_only_the_two_named</c> canh.
///
/// Hiện thực: composite <see cref="ModerationTargets"/> chọn <see cref="IModerationTargetProvider"/> theo loại — Content đăng ký
/// provider bài (bình luận khi GĐ3 đã có), Profile đăng ký provider người dùng.
/// </summary>
public interface IModerationTargets
{
    /// <summary>
    /// Có module nào đăng ký provider cho <paramref name="type"/> không (L-D13, GĐ6 D6 — chỉ-thêm). Bình luận trước khi GĐ3 merge
    /// → false: <c>POST /reports</c> trả 404 cùng thân lỗi với "không tồn tại", D7 chặn bằng bảng <c>decision × targetType</c>.
    /// Thêm provider bình luận là hàm này tự đúng — không sửa người gọi.
    /// </summary>
    bool Supports(ModerationTargetType type);

    /// <summary>Đọc, batch, KHÔNG theo BR-02 (Moderator thấy mọi thứ). Đối tượng không tồn tại → vắng mặt trong kết quả.</summary>
    Task<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<ModerationTarget> targets, CancellationToken ct = default);

    /// <summary>
    /// Luật "thấy được mới báo được" (Đ-6.12): <paramref name="actorId"/> có thấy đối tượng này không, theo đúng luật đọc của module
    /// chủ (bài: BR-02 + đang <c>published</c>). false cho cả "không tồn tại" — D6 trả 404 cho cả hai, cùng thân lỗi.
    /// </summary>
    Task<bool> CanViewAsync(Guid actorId, ModerationTarget target, CancellationToken ct = default);

    /// <summary>
    /// <c>published → hidden</c> kèm <paramref name="reasonCode"/>, trên kết nối + transaction <paramref name="tx"/> của người gọi.
    /// Lỗi NÉM ra — người gọi để nó rollback cả transaction. Loại không ẩn được (người dùng) → <see cref="NotSupportedException"/>.
    /// </summary>
    Task<HideOutcome> HideAsync(DbTransaction tx, ModerationTarget target, string reasonCode, CancellationToken ct = default);

    /// <summary><c>hidden → published</c>, xóa lý do, trên <paramref name="tx"/> của người gọi.</summary>
    Task<RestoreOutcome> RestoreAsync(DbTransaction tx, ModerationTarget target, CancellationToken ct = default);
}

/// <summary>
/// Hiện thực <see cref="IModerationTargets"/> cho MỘT loại đối tượng, ở module chủ của loại đó (đăng ký trong <c>Add&lt;X&gt;Module</c>
/// bằng <c>AddScoped&lt;IModerationTargetProvider, …&gt;</c>). Cùng hợp đồng, cùng luật: ghi chỉ trên <c>tx.Connection</c>, tham số hóa,
/// không <c>DbContext</c> thứ hai, không kết nối riêng (R6-06). Đặt ở <c>Infrastructure/</c> của module — nó chạm Npgsql.
/// </summary>
public interface IModerationTargetProvider
{
    ModerationTargetType Type { get; }

    /// <summary>Mọi phần tử của <paramref name="ids"/> cùng loại <see cref="Type"/>.</summary>
    Task<IReadOnlyDictionary<Guid, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<bool> CanViewAsync(Guid actorId, Guid id, CancellationToken ct);

    Task<HideOutcome> HideAsync(DbTransaction tx, Guid id, string reasonCode, CancellationToken ct);

    Task<RestoreOutcome> RestoreAsync(DbTransaction tx, Guid id, CancellationToken ct);
}
