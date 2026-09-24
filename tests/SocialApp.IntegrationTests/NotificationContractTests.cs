using SocialApp.Modules.Notification.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng cho nhóm <c>notification-v1</c> (GĐ6 Đ-6.1). Toàn bộ logic so sánh ở <see cref="ContractTestsBase"/>.
///
/// Ra đời CÙNG commit với bốn endpoint của nhóm (D11, L-D1 của hướng dẫn khối D): yaml mới mà thiếu lớp này thì
/// <see cref="ContractGateCoverageTests"/> đỏ; lớp này có mà nhóm chưa có endpoint nào thì là hợp đồng rỗng so Swagger rỗng — xanh mà
/// không chứng minh gì.
/// </summary>
[Trait("Category", "Contract")]
public sealed class NotificationContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "notification-v1.yaml";

    protected override string SwaggerGroupName => NotificationApiGroup.Name;
}
