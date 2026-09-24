using SocialApp.Modules.Messaging.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng REST của Messaging (GĐ5 Mục 10.5 #1): <c>messaging-v1.yaml</c> so với Swagger runtime của nhóm
/// <see cref="MessagingApiGroup.Name"/> — khung so sánh dùng chung ở <see cref="ContractTestsBase"/>.
/// </summary>
[Trait("Category", "Contract")]
public sealed class MessagingContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "messaging-v1.yaml";

    protected override string SwaggerGroupName => MessagingApiGroup.Name;
}
