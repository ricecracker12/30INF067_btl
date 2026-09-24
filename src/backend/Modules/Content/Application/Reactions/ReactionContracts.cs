using System.ComponentModel.DataAnnotations;
using FluentValidation;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Reactions;

/// <summary>
/// Body của <c>PUT …/reactions/me</c> (Đ-3.7). <c>ReactionType?</c> chứ không <c>ReactionType</c>: <c>default</c> của enum là
/// <see cref="ReactionType.Like"/>, nên body <c>{}</c> sẽ âm thầm thành "thả like" — cùng loại bẫy với <c>privacy</c> của Q-D2.
/// <c>[Required]</c> chỉ để Swagger ghi <c>required</c> (DataAnnotations đã tắt, validator dưới đây mới là thứ chặn).
/// </summary>
public sealed class SetReactionRequest
{
    [Required]
    public ReactionType? Type { get; init; }
}

/// <summary>
/// <c>type</c> vắng → 400 <c>errors.type</c>. Chuỗi lạ (<c>"heart"</c>) đã là 400 từ converter enum trước khi tới đây; số nguyên
/// ngoài tập (<c>99</c>) thì converter nhận — <c>IsInEnum</c> chặn nốt, không để nó rơi xuống CHECK của DB thành 500.
/// </summary>
public sealed class SetReactionRequestValidator : AbstractValidator<SetReactionRequest>
{
    public const string TypeInvalid = "Loại cảm xúc không hợp lệ.";

    public SetReactionRequestValidator()
    {
        RuleFor(x => x.Type).NotNull().IsInEnum().WithMessage(TypeInvalid);
    }
}

/// <summary>
/// Body 200 của mọi <c>PUT</c>/<c>DELETE …/reactions/me</c>: con số THẬT của server sau thao tác, để FE đối chiếu optimistic
/// update mà không phải nạp lại cả bài (Đ-3.7, Đ-3.13).
/// </summary>
/// <param name="ReactionCounts">Không bao giờ <c>null</c>; không có khóa giá trị 0 (Đ-3.8).</param>
/// <param name="MyReaction">Cảm xúc của người gọi sau thao tác, <c>null</c> sau <c>DELETE</c>.</param>
public sealed record ReactionSummary(IReadOnlyDictionary<string, int> ReactionCounts, ReactionType? MyReaction);

/// <summary>Kết quả của một lần <see cref="IReactionStore.ApplyAsync"/>: thao tác đã làm và bộ đếm sau cùng của đối tượng.</summary>
public sealed record ReactionApplied(ReactionChange Change, IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// C2/C3 GĐ3 — MỘT khuôn giao dịch cho mọi lần thả / đổi / gỡ cảm xúc (Đ-3.8). Hiện thực ở <c>Infrastructure/Persistence</c>.
/// </summary>
public interface IReactionStore
{
    /// <summary>
    /// Trong MỘT transaction: khóa dòng đối tượng (<c>FOR UPDATE</c>, chỉ khi bài <c>published</c> / bình luận <c>visible</c>) →
    /// đọc cảm xúc hiện tại → <see cref="ReactionTransition.Apply"/> → ghi dòng <c>reactions</c> → cập nhật <c>reaction_counts</c>
    /// bằng MỘT câu SQL nguyên tử trên <c>jsonb</c>. Không chạm <c>updated_at</c>/<c>edited_at</c> của đối tượng.
    /// </summary>
    /// <param name="desired">Loại muốn có sau request; <c>null</c> = gỡ.</param>
    /// <returns><c>null</c> khi không khóa được đối tượng (không tồn tại, đã xóa, bị ẩn) — người gọi trả 404.</returns>
    Task<ReactionApplied?> ApplyAsync(
        ReactionTargetType targetType, Guid targetId, Guid actorId, ReactionType? desired, DateTimeOffset now, CancellationToken ct);
}

/// <summary>
/// C4 GĐ3 (Đ-3.11) — <c>myReaction</c> theo LÔ: một trang (bài hay bình luận) → đúng MỘT câu truy vấn đi thẳng vào PK
/// <c>(user_id, target_type, target_id)</c>. Không có bản đơn: bản đơn "cho tiện" là N+1 ở feed.
/// </summary>
public interface IReactionReader
{
    /// <returns>Chỉ các đối tượng người gọi CÓ cảm xúc; đối tượng vắng mặt nghĩa là <c>myReaction = null</c>.</returns>
    Task<IReadOnlyDictionary<Guid, ReactionType>> GetMineAsync(
        Guid actorId, ReactionTargetType targetType, IReadOnlyCollection<Guid> targetIds, CancellationToken ct);
}
