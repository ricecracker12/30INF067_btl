using FluentValidation;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Lớp "sai dạng → 400" của Đ-2.8, và là lớp DUY NHẤT của D3 không cần I/O.
///
/// Ba lớp chạy theo đúng thứ tự này, mỗi lớp một mã khác nhau, và thứ tự là cố ý:
/// <list type="number">
/// <item><b>Sai dạng → 400</b> (lớp này, FluentValidation, trước cả khi vào service).</item>
/// <item><b>Tiền tố của người khác → 403</b> (<c>ProfileService.SetAvatarAsync</c>, Đ-2.7).</item>
/// <item><b>Object chưa có / sai loại → 400</b> (HEAD lên R2, chỉ biết được sau I/O).</item>
/// </list>
///
/// Vì sao lớp 1 phải đứng trước lớp 2 dù cả hai đều nhìn vào chuỗi key: một key rác (<c>"../../etc/passwd"</c>) mà rơi
/// thẳng vào <c>StorageKeys.BelongsTo</c> thì chỉ nhận 403 — trả lời sai câu hỏi, và nói với người gõ nhầm rằng họ đang
/// bị từ chối quyền.
/// </summary>
public sealed class SetAvatarRequestValidator : AbstractValidator<SetAvatarRequest>
{
    /// <summary>
    /// Chép NGUYÊN <c>SetAvatarRequest.mediaKey.pattern</c> của <c>profile-v1.yaml</c> — hợp đồng là nguồn sự thật, và
    /// regex gõ lại theo trí nhớ thì client hợp lệ bị chặn mà không ai biết vì sao.
    ///
    /// <c>[0-9a-f-]{36}</c> là Guid dạng "D" viết thường; <c>[0-9a-f]{32}</c> là UUID v7 dạng "N" mà
    /// <c>StorageKeys.ForAvatar</c> sinh ra. Đuôi lấy từ allowlist ba loại ảnh (Đ-2.8) — <b>không</b> lấy từ tên file
    /// client gửi lên.
    /// </summary>
    public const string MediaKeyPattern = @"^avatars/[0-9a-f-]{36}/[0-9a-f]{32}\.(jpg|png|webp)$";

    /// <summary>
    /// Một câu cho mọi cách sai dạng. KHÔNG nhắc lại regex trong thông điệp: người dùng cuối không đọc regex, và client
    /// hợp lệ không bao giờ thấy câu này — key là thứ do <c>POST /media/uploads</c> cấp, không phải thứ người ta gõ.
    /// </summary>
    public const string MediaKeyInvalid = "Khóa ảnh không đúng dạng.";

    public SetAvatarRequestValidator() =>
        RuleFor(x => x.MediaKey).Matches(MediaKeyPattern).WithMessage(MediaKeyInvalid);
}
