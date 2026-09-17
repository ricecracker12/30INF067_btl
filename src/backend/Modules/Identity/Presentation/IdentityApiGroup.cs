namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Định danh nhóm Swagger của module Identity — bề mặt HTTP mà module công bố cho host.
///
/// Đặt ở <c>Presentation</c> chứ không ở <c>DependencyInjection</c>: "nhóm Swagger" là từ vựng của
/// giao thức HTTP, mà DI là chỗ ráp dịch vụ chứ không phải chỗ giữ kiến thức giao thức. Để nhầm ở
/// đó thì bề mặt DI dần biến thành cái sọt đựng mọi hằng số cần chia sẻ với host, và tầng nào sở
/// hữu cái gì hết còn ranh giới.
///
/// <see cref="Name"/> xuất hiện ở ba nơi và phải khớp cả ba: <c>[ApiExplorerSettings]</c> trên
/// controller, <c>SwaggerDoc</c> ở Program.cs, và tên file hợp đồng <c>identity-v1.yaml</c> cùng
/// thư mục. Lệch một chỗ thì endpoint biến mất khỏi Swagger mà không có lỗi nào —
/// <c>PresentationBoundaryTests</c> và <c>IdentityContractTests</c> canh chuyện đó.
/// </summary>
public static class IdentityApiGroup
{
    /// <summary>Tên nhóm, trùng tên file hợp đồng <c>identity-v1.yaml</c>.</summary>
    public const string Name = "identity-v1";

    /// <summary>Nhãn hiển thị trên dropdown của Swagger UI.</summary>
    public const string Title = "Identity";
}
