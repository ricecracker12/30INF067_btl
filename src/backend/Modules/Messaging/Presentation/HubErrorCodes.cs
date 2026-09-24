using SocialApp.Modules.Messaging.Application;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Mã lỗi hub (chat-hub-v1.md Mục 2) — <c>HubException.Message</c> là MÃ, không phải câu cho người đọc; FE ánh xạ sang câu tiếng
/// Việt. Tập này phải BẰNG mảng <c>errors</c> của <c>chat-hub-v1.examples.json</c> (B4).
/// </summary>
public static class HubErrorCodes
{
    public const string Forbidden = "forbidden";
    public const string NotFriends = "not-friends";
    public const string Validation = "validation";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate-limited";
    public const string Unavailable = "unavailable";

    public static readonly string[] All = [Forbidden, NotFriends, Validation, Conflict, RateLimited, Unavailable];

    /// <summary>Dịch lỗi nghiệp vụ sang mã hub. Lỗi lạ → <see cref="Unavailable"/> (client gửi lại, Đ-5.5 lo phần trùng).</summary>
    public static string From(Error error) => error.Code switch
    {
        _ when error.Code == Error.Forbidden.Code => Forbidden,
        _ when error.Code == MessagingErrors.NotFriends.Code => NotFriends,
        _ when error.Code == MessagingErrors.ClientMsgIdReused.Code => Conflict,
        _ when error.Status == 400 => Validation,
        _ => Unavailable,
    };
}
