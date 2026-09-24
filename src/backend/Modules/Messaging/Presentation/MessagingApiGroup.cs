namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Định danh nhóm Swagger của module Messaging (khuôn <c>SocialGraphApiGroup</c>). <see cref="Name"/> phải khớp ở BA chỗ:
/// <c>[ApiExplorerSettings]</c> trên controller, <c>SwaggerDoc</c> ở Program.cs, và tên file hợp đồng <c>messaging-v1.yaml</c>
/// cùng thư mục. Lệch một chỗ thì <c>/swagger/messaging-v1/swagger.json</c> 404 và cổng hợp đồng đỏ với thông báo không chỉ vào
/// nguyên nhân thật.
///
/// Hub <c>/hubs/chat</c> KHÔNG có trong Swagger — hợp đồng của nó là <c>chat-hub-v1.md</c> + <c>chat-hub-v1.examples.json</c>.
/// </summary>
public static class MessagingApiGroup
{
    /// <summary>Tên nhóm, trùng tên file hợp đồng <c>messaging-v1.yaml</c>.</summary>
    public const string Name = "messaging-v1";

    /// <summary>Nhãn hiển thị trên dropdown của Swagger UI.</summary>
    public const string Title = "Messaging";
}
