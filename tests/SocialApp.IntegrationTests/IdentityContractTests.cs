using SocialApp.Modules.Identity.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng cho module Identity. Toàn bộ logic so sánh ở <see cref="ContractTestsBase"/> — đọc lý do và ba luật
/// bắt buộc của lớp con ở đó.
///
/// Sáu endpoint auth của GĐ1 đã ráp xong từ D11, nên cổng này chặn HAI chiều từ lúc đó (trước khi gỡ <c>Skip</c> đã
/// thấy nó đỏ đúng lý do ở cả ba phần: thiếu operation, thiếu status code, lệch required — thi công D11). Ở B4 (GĐ2)
/// lớp này chỉ mất phần thân, không mất một khẳng định nào.
/// </summary>
[Trait("Category", "Contract")]
public sealed class IdentityContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "identity-v1.yaml";

    protected override string SwaggerGroupName => IdentityApiGroup.Name;
}
