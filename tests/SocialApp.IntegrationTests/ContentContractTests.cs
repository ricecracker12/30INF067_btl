using SocialApp.Modules.Content.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng cho module Content (B4, GĐ2) — sáu endpoint của D4–D8. Toàn bộ logic so sánh ở
/// <see cref="ContractTestsBase"/>; đọc ba luật bắt buộc của lớp con ở đó trước khi thêm module thứ tư.
///
/// MỘT nhóm Swagger cho cả <c>MediaController</c> lẫn <c>PostsController</c>: nhóm bám theo MODULE, không theo
/// controller — nên một lớp con ở đây canh cả hai.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ContentContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "content-v1.yaml";

    protected override string SwaggerGroupName => ContentApiGroup.Name;
}
