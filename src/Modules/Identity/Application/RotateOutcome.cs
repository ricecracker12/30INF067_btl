namespace SocialApp.Modules.Identity.Application;

/// <summary>Kết quả xoay vòng refresh token (giai-doan-1.md Mục 7.3). Chỉ hai nhánh đầu phát được token mới.</summary>
public abstract record RotateOutcome
{
    private RotateOutcome()
    {
    }

    /// <summary>Bước 5: token hợp lệ đã được thay bằng token mới cùng family. <paramref name="RoleCode"/> đọc từ DB.</summary>
    public sealed record Rotated(Guid UserId, string RoleCode) : RotateOutcome;

    /// <summary>Bước 3a (Đ-D3): token vừa bị xoay trong ân hạn 10 giây, family còn sống → phát token anh em cùng family.</summary>
    public sealed record Grace(Guid UserId, string RoleCode) : RotateOutcome;

    /// <summary>Bước 3b: token đã xoay/thu hồi bị dùng lại ngoài ân hạn → cả family ĐÃ bị thu hồi và COMMIT.</summary>
    public sealed record ReuseDetected(Guid UserId) : RotateOutcome;

    /// <summary>Bước 2 hoặc 4: không tồn tại, hoặc hết hạn (không phải reuse — family giữ nguyên).</summary>
    public sealed record Invalid : RotateOutcome
    {
        public static readonly Invalid Instance = new();
    }
}
