using Microsoft.AspNetCore.WebUtilities;

namespace SocialApp.SharedKernel.Errors;

/// <summary>
/// <c>title</c> và <c>type</c> MẶC ĐỊNH theo status cho mọi Problem Details của app — MỘT bảng cho bốn nguồn sinh lỗi: Result
/// (<c>ToActionResult</c>), validation 400, status code pages (401/403/404/429 do middleware sinh), và ClientErrorMapping của MVC
/// (415, <c>NotFound()</c>…). Để framework tự điền thì mỗi nguồn một kiểu: "Conflict", "One or more validation errors occurred.",
/// 410/423 KHÔNG có title — trong khi hợp đồng <c>required: [title, status, traceId]</c> (D9).
///
/// Lỗi nghiệp vụ cần title riêng (hợp đồng dùng title khác nhau cho cùng 401 của login và refresh) thì đặt
/// <see cref="Results.Error.Title"/>; bảng này chỉ là giá trị khi không đặt. Không chứa dữ liệu người dùng.
/// </summary>
public static class ProblemTitles
{
    public const string BadRequest = "Dữ liệu không hợp lệ";
    public const string Unauthorized = "Chưa xác thực";
    public const string Forbidden = "Bị từ chối";
    public const string NotFound = "Không tìm thấy tài nguyên";
    public const string MethodNotAllowed = "Phương thức không được hỗ trợ";
    public const string Conflict = "Xung đột dữ liệu";
    public const string Gone = "Tài nguyên không còn hiệu lực";
    public const string UnsupportedMediaType = "Kiểu nội dung không được hỗ trợ";
    public const string Locked = "Tài nguyên đang bị khóa";
    public const string TooManyRequests = "Quá nhiều yêu cầu";
    public const string InternalError = "Đã xảy ra lỗi không mong muốn";

    /// <summary><c>detail</c> của 400 validation — cùng chuỗi với <see cref="AppException.Validation"/>.</summary>
    public const string ValidationDetail = "Dữ liệu đầu vào không hợp lệ";

    /// <summary>Các status có title tiếng Việt trong bảng — nạp vào ClientErrorMapping của MVC.</summary>
    public static readonly int[] KnownStatuses = [400, 401, 403, 404, 405, 409, 410, 415, 423, 429, 500];

    public static string For(int status) => status switch
    {
        400 => BadRequest,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        405 => MethodNotAllowed,
        409 => Conflict,
        410 => Gone,
        415 => UnsupportedMediaType,
        423 => Locked,
        429 => TooManyRequests,
        >= 500 => InternalError,
        // Status hiếm chưa có trong bảng: vẫn có title (hợp đồng bắt buộc), dùng reason phrase chuẩn.
        _ => ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : "Lỗi",
    };

    /// <summary><c>type</c> theo hợp đồng: <c>https://httpstatuses.io/{status}</c>.</summary>
    public static string TypeFor(int status) => $"https://httpstatuses.io/{status}";
}
