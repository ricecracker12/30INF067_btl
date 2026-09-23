using SocialApp.Modules.SocialGraph.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Cổng hợp đồng cho module SocialGraph (B5, GĐ4) — bảy endpoint của D1–D6. Toàn bộ logic so sánh ở
/// <see cref="ContractTestsBase"/>; đọc ba luật bắt buộc của lớp con ở đó.
///
/// MỘT nhóm Swagger cho cả <c>FriendsController</c>, <c>FollowsController</c> lẫn <c>RelationshipsController</c>:
/// nhóm bám theo MODULE, không theo controller — nên một lớp con ở đây canh cả ba.
/// </summary>
[Trait("Category", "Contract")]
public sealed class SocialGraphContractTests(ApiFactory factory)
    : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "socialgraph-v1.yaml";

    protected override string SwaggerGroupName => SocialGraphApiGroup.Name;
}
