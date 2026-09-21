using SocialApp.Modules.Profile.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng cho module Profile (B4, GĐ2) — bốn endpoint của D1–D3. Toàn bộ logic so sánh ở
/// <see cref="ContractTestsBase"/>; đọc ba luật bắt buộc của lớp con ở đó trước khi thêm module thứ tư.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ProfileContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "profile-v1.yaml";

    protected override string SwaggerGroupName => ProfileApiGroup.Name;
}
