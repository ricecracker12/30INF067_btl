using SocialApp.SharedKernel.Errors;

namespace SocialApp.SharedKernel.Results;

/// <summary>
/// Kết quả thao tác không kèm dữ liệu. Dùng ở tầng Application để tránh ném exception cho luồng
/// nghiệp vụ thường gặp; controller ánh xạ sang HTTP bằng <c>result.ToActionResult(this)</c>.
/// </summary>
public readonly record struct Result(bool IsSuccess, Error? Error)
{
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Từ chối ở tầng 3 (ownership/quan hệ). Xem <see cref="Results.Error.Forbidden"/>.</summary>
    public static Result Forbidden() => Failure(global::SocialApp.SharedKernel.Results.Error.Forbidden);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Kết quả thao tác kèm dữ liệu <typeparamref name="T"/> khi thành công.</summary>
public readonly record struct Result<T>(bool IsSuccess, T? Value, Error? Error)
{
    public bool IsFailure => !IsSuccess;

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);

    /// <summary>Từ chối ở tầng 3 (ownership/quan hệ). Xem <see cref="Results.Error.Forbidden"/>.</summary>
    public static Result<T> Forbidden() => Failure(global::SocialApp.SharedKernel.Results.Error.Forbidden);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>
/// Mô tả lỗi nghiệp vụ (mã + thông điệp + status HTTP gợi ý) — trung lập với tầng HTTP. <paramref name="Title"/> là nhãn ngắn
/// ổn định theo LOẠI lỗi (<c>title</c> của Problem Details); để trống thì dùng mặc định theo status
/// (<see cref="Errors.ProblemTitles"/>). Cả Message lẫn Title KHÔNG chứa dữ liệu người dùng.
///
/// <paramref name="Errors"/> là tham số CUỐI và có mặc định (Q-D4, chốt 2026-09-19): mọi lời gọi vị trí đã có của GĐ1
/// (<c>IdentityErrors</c>) không phải đổi một ký tự nào. Chỉ 400 theo TRƯỜNG mới đặt nó — dựng bằng
/// <see cref="Validation"/>, đừng tự ghép dictionary ở module.
/// </summary>
public readonly record struct Error(
    string Code, string Message, int Status, string? Title = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    /// <summary>
    /// Một thông điệp duy nhất cho mọi 403 tầng 3 — không nêu id. Service trả CÙNG lỗi này cho "không tồn
    /// tại" và "không phải của bạn" ở thao tác cần ownership (giai-doan-1.md Mục 6.3 quy ước 3, 3b): trả 404
    /// cho một bên và 403 cho bên kia thì status code đã lộ tài nguyên có tồn tại.
    /// </summary>
    public static readonly Error Forbidden = new("auth.forbidden", "Bạn không có quyền thực hiện thao tác này.", 403);

    /// <summary>
    /// 400 theo TRƯỜNG, cùng hình dạng với 400 của FluentValidation: title "Dữ liệu không hợp lệ", detail
    /// <see cref="ProblemTitles.ValidationDetail"/>, <c>errors</c> dạng <c>{field: [message]}</c>. Dành cho lỗi chỉ biết được
    /// SAU I/O nên FluentValidation không kiểm được: HEAD lệch khai báo (D3, D5), BR-01 với <c>media_count</c> đọc từ DB (D7).
    ///
    /// Vì sao không phải <c>AppException.Validation</c>: ném exception cho luồng nghiệp vụ thường gặp là thứ quy ước 2 của
    /// GĐ1 cấm. Vì sao không phải <c>ModelState.AddModelError</c> ở controller: nó đem bảng ánh xạ lỗi → HTTP ra khỏi
    /// <c>ResultHttpExtensions</c>, chỗ DUY NHẤT được phép giữ bảng đó.
    /// </summary>
    /// <param name="field">Tên trường như client gửi, camelCase theo hợp đồng (<c>mediaKey</c>, <c>mediaKeys</c>, <c>body</c>).</param>
    /// <param name="message">Câu tiếng Việt cho người dùng cuối — KHÔNG chứa id, key, email hay tên kiểu .NET.</param>
    public static Error Validation(string field, string message) =>
        new("validation", ProblemTitles.ValidationDetail, 400, null,
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
