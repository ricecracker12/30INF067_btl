namespace SocialApp.Modules.Profile.Domain;

/// <summary>
/// Hồ sơ người dùng (ENT-01a, bảng <c>profile.profiles</c>).
///
/// Tên kiểu là <c>UserProfile</c> chứ không phải <c>Profile</c> như B.3 viết: từ trong
/// <c>SocialApp.Modules.Profile.Infrastructure</c>, C# tra tên <c>Profile</c> theo thứ tự namespace
/// lồng từ trong ra ngoài nên gặp NAMESPACE <c>SocialApp.Modules.Profile</c> trước khi xét
/// <c>using</c> của file — <c>DbSet&lt;Profile&gt;</c> là CS0118 ("is a namespace but is used like a
/// type"). Đường thoát còn lại là alias <c>using ProfileEntity = ...</c> ở mọi file của GĐ2–GĐ8;
/// đổi tên kiểu rẻ hơn. B.3 đã được sửa theo trong cùng commit này.
///
/// KHÔNG có navigation property trỏ sang <c>User</c> — không có kiểu nào để trỏ (Đ-2.2, Đ-2.3).
/// Đây là ranh giới module, không phải "tạm thời chưa làm": module Content và Profile chỉ gặp
/// Identity qua <c>uuid</c> trần.
/// </summary>
public sealed class UserProfile
{
    /// <summary>Độ dài tối thiểu của tên hiển thị (Đ-2.4). Validator của D2 dùng lại hằng số này.</summary>
    public const int DisplayNameMinLength = 2;

    /// <summary>Độ dài tối đa của tên hiển thị (Đ-2.4, cột <c>varchar(50)</c>).</summary>
    public const int DisplayNameMaxLength = 50;

    /// <summary>
    /// Khóa chính, <b>bằng</b> <c>identity.users.user_id</c> — không FK chéo schema (Đ-2.2).
    ///
    /// KHÔNG có <c>= Uuid7.New()</c>: khác <c>Post</c>, khóa chính của hồ sơ không do module này
    /// sinh ra mà luôn lấy từ <c>User.GetUserId()</c> của token ở tầng D. Gán giá trị mặc định ở đây
    /// là tạo ra một hồ sơ mồ côi không thuộc về ai, và không test nào bắt được vì kiểu vẫn đúng.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Tên hiển thị, <c>varchar(50)</c> NOT NULL. DB có CHECK <c>ck_profiles_display_name_not_blank</c>
    /// canh lại chuỗi toàn khoảng trắng; ràng buộc 2–50 ký tự là việc của validator ở D2.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>Giới thiệu ngắn, <c>varchar(500)</c>; <c>null</c> nghĩa là chưa đặt.</summary>
    public string? Bio { get; set; }

    /// <summary>
    /// <c>storage_key</c> của ảnh đại diện trên R2, <c>varchar(200)</c>; <c>null</c> = dùng ảnh mặc
    /// định. Khối A không chạm R2 — ở đây nó chỉ là chuỗi.
    /// </summary>
    public string? AvatarKey { get; set; }

    /// <summary>Thời điểm tạo, do đồng hồ app gán; <c>DEFAULT now()</c> của DB chỉ là lưới cho SQL thô.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm sửa gần nhất — do override <c>SaveChanges</c> của <c>ProfileDbContext</c> (A2) đóng dấu.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
