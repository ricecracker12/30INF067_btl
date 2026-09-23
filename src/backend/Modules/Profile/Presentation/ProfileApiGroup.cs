namespace SocialApp.Modules.Profile.Presentation;

/// <summary>
/// Định danh nhóm Swagger của module Profile — bề mặt HTTP mà module công bố cho host. Chép nguyên hình
/// dạng <c>IdentityApiGroup</c> của GĐ1; GĐ2 không phát minh khuôn mới.
///
/// Đặt ở <c>Presentation</c> chứ không ở <c>DependencyInjection</c>: "nhóm Swagger" là từ vựng của giao
/// thức HTTP, mà DI là chỗ ráp dịch vụ chứ không phải chỗ giữ kiến thức giao thức.
///
/// <see cref="Name"/> xuất hiện ở ba nơi và phải khớp cả ba: <c>[ApiExplorerSettings]</c> trên controller,
/// <c>SwaggerDoc</c> ở Program.cs, và tên file hợp đồng <c>profile-v1.yaml</c> cùng thư mục. Lệch một chỗ
/// thì <c>/swagger/profile-v1/swagger.json</c> trả 404 và cổng hợp đồng (B4) đỏ với thông báo "không parse
/// được JSON" — không chỉ vào nguyên nhân thật.
/// </summary>
public static class ProfileApiGroup
{
    /// <summary>Tên nhóm, trùng tên file hợp đồng <c>profile-v1.yaml</c>.</summary>
    public const string Name = "profile-v1";

    /// <summary>Nhãn hiển thị trên dropdown của Swagger UI.</summary>
    public const string Title = "Profile";
}
