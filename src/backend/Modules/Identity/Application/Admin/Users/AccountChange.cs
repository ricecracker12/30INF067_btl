using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Body của <c>POST /admin/users/{userId}/lock</c> (schema <c>LockRequest</c>). <paramref name="Reason"/> vào <c>metadata.reason</c>
/// của audit (L-D9), không lưu ở <c>users</c>, KHÔNG vào log (B.10 #5).
///
/// Class <c>init</c>, không từ khóa <c>required</c>: thiếu <c>reason</c> phải tới FluentValidation (câu tiếng Việt), không bị
/// System.Text.Json chặn trước. <c>[Required]</c> chỉ để Swagger ghi <c>required: [reason]</c> — DataAnnotations đã tắt ở host.
/// </summary>
public sealed class LockRequest
{
    [Required]
    public string? Reason { get; init; }
}

/// <summary><c>reason</c> sau khi cắt khoảng trắng hai đầu dài 1–500 ký tự (Mục 8.2).</summary>
public sealed class LockRequestValidator : AbstractValidator<LockRequest>
{
    public const int MaxReasonLength = 500;

    public const string ReasonRequired = "Lý do khóa là bắt buộc.";

    public static readonly string ReasonTooLong = $"Lý do khóa tối đa {MaxReasonLength} ký tự.";

    public LockRequestValidator()
    {
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage(ReasonRequired);

        RuleFor(x => x.Reason)
            .Must(r => r is null || r.Trim().Length <= MaxReasonLength)
            .WithMessage(ReasonTooLong);
    }
}

/// <summary>
/// Body của <c>PUT /admin/users/{userId}/role</c> (schema <c>AssignRoleRequest</c>, D4). Chỉ mã vai trò — <c>code</c> bất biến
/// (Đ-6.9), so CHÍNH XÁC phân biệt hoa thường. Cùng luật <c>[Required]</c>/<c>init</c> như <see cref="LockRequest"/>.
/// </summary>
public sealed class AssignRoleRequest
{
    [Required]
    public string? RoleCode { get; init; }
}

/// <summary>
/// <c>roleCode</c> không rỗng, tối đa 30 ký tự (<c>roles.code varchar(30)</c>). Không kiểm pattern ở đây: mã sai dạng thì cũng không
/// tồn tại — store trả 400 "Vai trò không tồn tại." cho cả hai, một câu thay vì hai.
/// </summary>
public sealed class AssignRoleRequestValidator : AbstractValidator<AssignRoleRequest>
{
    public const int MaxRoleCodeLength = 30;

    public const string RoleCodeRequired = "Vai trò là bắt buộc.";

    public static readonly string RoleCodeTooLong = $"Mã vai trò tối đa {MaxRoleCodeLength} ký tự.";

    public AssignRoleRequestValidator()
    {
        RuleFor(x => x.RoleCode)
            .Must(c => !string.IsNullOrWhiteSpace(c))
            .WithMessage(RoleCodeRequired);

        RuleFor(x => x.RoleCode)
            .Must(c => c is null || c.Length <= MaxRoleCodeLength)
            .WithMessage(RoleCodeTooLong);
    }
}

/// <summary>
/// Giá trị <c>revocation</c> của <c>AdminUserChange</c> (Đ-6.6). Chuỗi chứ không enum C#: converter enum của host ghi camelCase
/// (<c>notNeeded</c>), còn hợp đồng là <c>not-needed</c>.
/// </summary>
public static class RevocationStates
{
    /// <summary>Mốc <c>revoked:user</c> đã ghi — request kế tiếp của người đó 401.</summary>
    public const string Applied = "applied";

    /// <summary>
    /// DB đã đổi nhưng ghi Redis hỏng sau 3 lần: phiên đang mở giữ quyền cũ tối đa 15 phút. UI Admin nói thật điều đó.
    /// </summary>
    public const string Deferred = "deferred";

    /// <summary>Không có gì để thu hồi: mở khóa, hoặc thao tác không đổi gì (L-D10).</summary>
    public const string NotNeeded = "not-needed";
}

/// <summary>Body 200 của mọi thao tác ghi lên một tài khoản (<c>lock</c>, <c>unlock</c>, D4 <c>role</c>) — schema <c>AdminUserChange</c>.</summary>
public sealed record AdminUserChange(AdminUser User, string Revocation);
