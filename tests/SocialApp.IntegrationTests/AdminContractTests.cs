using SocialApp.Modules.Identity.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng cho nhóm <c>admin-v1</c> — nhóm Swagger thứ hai của Identity (GĐ6 Đ-6.1). Toàn bộ logic so sánh ở
/// <see cref="ContractTestsBase"/>.
///
/// Ra đời CÙNG commit với endpoint đầu tiên của nhóm (D2, L-D1 của hướng dẫn khối D): yaml mới mà thiếu lớp này thì
/// <see cref="ContractGateCoverageTests"/> đỏ; lớp này có mà nhóm chưa có endpoint nào thì là hợp đồng rỗng so Swagger rỗng —
/// xanh mà không chứng minh gì.
/// </summary>
[Trait("Category", "Contract")]
public sealed class AdminContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "admin-v1.yaml";

    protected override string SwaggerGroupName => AdminApiGroup.Name;
}
