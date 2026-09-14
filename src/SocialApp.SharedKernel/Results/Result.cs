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

/// <summary>Mô tả lỗi nghiệp vụ (mã + thông điệp + status HTTP gợi ý) — trung lập với tầng HTTP.</summary>
public readonly record struct Error(string Code, string Message, int Status)
{
    /// <summary>
    /// Một thông điệp duy nhất cho mọi 403 tầng 3 — không nêu id. Service trả CÙNG lỗi này cho "không tồn
    /// tại" và "không phải của bạn" ở thao tác cần ownership (giai-doan-1.md Mục 6.3 quy ước 3, 3b): trả 404
    /// cho một bên và 403 cho bên kia thì status code đã lộ tài nguyên có tồn tại.
    /// </summary>
    public static readonly Error Forbidden = new("auth.forbidden", "Bạn không có quyền thực hiện thao tác này.", 403);
}
